using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Base class for all Docker test resources. Provides shared lifecycle,
  /// diagnostics, cleanup, and hook infrastructure.
  /// </summary>
  public abstract class ResourceBase : IDockerResource
  {
    private readonly List<Func<IDockerResource, Task>> _beforeInitHooks = new();
    private readonly List<Func<IDockerResource, Task>> _afterReadyHooks = new();
    private readonly List<Func<IDockerResource, Task>> _beforeDisposeHooks = new();
    private readonly List<Func<IDockerResource, Task>> _afterDisposeHooks = new();

    /// <summary>
    /// Creates a new resource with the given kernel and options.
    /// </summary>
    protected ResourceBase(FluentDockerKernel kernel, DockerResourceOptions options = null)
    {
      Kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
      Options = options ?? new DockerResourceOptions();
    }

    /// <summary>
    /// The kernel managing drivers for this resource.
    /// </summary>
    public FluentDockerKernel Kernel { get; }

    /// <summary>
    /// Resource configuration.
    /// </summary>
    public DockerResourceOptions Options { get; }

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// The resolved driver ID for this resource.
    /// </summary>
    public string DriverId { get; private set; }

    /// <summary>
    /// Unique name generated for this resource. Set during initialization.
    /// </summary>
    public string ResourceName { get; protected set; }

    /// <summary>
    /// Diagnostics collected on failure.
    /// </summary>
    public ResourceDiagnostics Diagnostics { get; private set; }

    #region Lifecycle Hooks

    /// <summary>
    /// Adds a hook invoked before initialization starts.
    /// </summary>
    public ResourceBase OnBeforeInitialize(Func<IDockerResource, Task> hook)
    {
      _beforeInitHooks.Add(hook);
      return this;
    }

    /// <summary>
    /// Adds a hook invoked after the resource is ready.
    /// </summary>
    public ResourceBase OnAfterReady(Func<IDockerResource, Task> hook)
    {
      _afterReadyHooks.Add(hook);
      return this;
    }

    /// <summary>
    /// Adds a hook invoked before disposal starts.
    /// </summary>
    public ResourceBase OnBeforeDispose(Func<IDockerResource, Task> hook)
    {
      _beforeDisposeHooks.Add(hook);
      return this;
    }

    /// <summary>
    /// Adds a hook invoked after disposal completes.
    /// </summary>
    public ResourceBase OnAfterDispose(Func<IDockerResource, Task> hook)
    {
      _afterDisposeHooks.Add(hook);
      return this;
    }

    #endregion

    #region IDockerResource

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
      if (IsInitialized)
        return;

      DriverId = ResolveDriverId();

      using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      cts.CancelAfter(Options.InitializationTimeout);

      try
      {
        await RunHooksAsync(_beforeInitHooks, cts.Token);
        await PreflightAsync(cts.Token);
        await ProvisionAsync(cts.Token);
        IsInitialized = true;
        await RunHooksAsync(_afterReadyHooks, cts.Token);
      }
      catch (Exception ex)
      {
        Diagnostics = await CollectDiagnosticsAsync(ex);
        throw;
      }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      try
      {
        await RunHooksAsync(_beforeDisposeHooks, CancellationToken.None);
      }
      catch
      {
        // Hooks should not prevent cleanup
      }

      try
      {
        await TeardownAsync();
      }
      catch
      {
        if (Options.ForceRemoveOnDispose)
        {
          try { await ForceRemoveAsync(); }
          catch { /* best effort */ }
        }
      }

      IsInitialized = false;

      try
      {
        await RunHooksAsync(_afterDisposeHooks, CancellationToken.None);
      }
      catch
      {
        // Hooks should not throw after cleanup
      }
    }

    #endregion

    #region Template Methods

    /// <summary>
    /// Checks that the driver supports the required capabilities for this resource type.
    /// </summary>
    protected abstract Task PreflightAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates, starts, and waits for the resource to be ready.
    /// </summary>
    protected abstract Task ProvisionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gracefully stops and removes the resource.
    /// </summary>
    protected abstract Task TeardownAsync();

    /// <summary>
    /// Force-removes the resource when graceful teardown fails.
    /// </summary>
    protected abstract Task ForceRemoveAsync();

    /// <summary>
    /// Collects diagnostic information when initialization fails.
    /// </summary>
    protected virtual Task<ResourceDiagnostics> CollectDiagnosticsAsync(Exception failure)
    {
      return Task.FromResult(new ResourceDiagnostics
      {
        Failure = failure,
        ResourceName = ResourceName,
        DriverId = DriverId
      });
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Resolves which driver ID to use based on <see cref="Options"/>.
    /// </summary>
    protected string ResolveDriverId()
    {
      if (Options.Driver.UseDefault)
        return Kernel.DefaultDriverId;

      return Options.Driver.DriverId
             ?? throw new InvalidOperationException("DriverSelection has no DriverId set");
    }

    /// <summary>
    /// Generates a unique name for parallel-safe resource creation.
    /// </summary>
    protected static string GenerateUniqueName(string prefix)
    {
      return $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(63, prefix.Length + 33)];
    }

    private static async Task RunHooksAsync(
        List<Func<IDockerResource, Task>> hooks,
        CancellationToken cancellationToken)
    {
      // Not using the cancellation token to cancel individual hooks,
      // but the linked CTS in InitializeAsync will cancel the whole operation.
      foreach (var hook in hooks)
      {
        cancellationToken.ThrowIfCancellationRequested();
        await hook(null); // Pass null to avoid confusion; callers use closures.
      }
    }

    #endregion
  }

  /// <summary>
  /// Diagnostic information collected when a resource fails to initialize.
  /// </summary>
  public class ResourceDiagnostics
  {
    /// <summary>
    /// The exception that caused the failure.
    /// </summary>
    public Exception Failure { get; set; }

    /// <summary>
    /// Resource name at the time of failure.
    /// </summary>
    public string ResourceName { get; set; }

    /// <summary>
    /// Driver ID used.
    /// </summary>
    public string DriverId { get; set; }

    /// <summary>
    /// Container/service inspect payload (JSON), if available.
    /// </summary>
    public string InspectPayload { get; set; }

    /// <summary>
    /// Logs collected from the resource, if available.
    /// </summary>
    public string Logs { get; set; }

    /// <summary>
    /// Additional context about the operation.
    /// </summary>
    public string OperationContext { get; set; }
  }
}
