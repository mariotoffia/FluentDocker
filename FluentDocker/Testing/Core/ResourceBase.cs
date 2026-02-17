using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
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
  public abstract class ResourceBase : ITestResource
  {
    private readonly List<Func<ITestResource, Task>> _beforeInitHooks = new();
    private readonly List<Func<ITestResource, Task>> _afterReadyHooks = new();
    private readonly List<Func<ITestResource, Task>> _beforeDisposeHooks = new();
    private readonly List<Func<ITestResource, Task>> _afterDisposeHooks = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private bool _provisioned;

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
    public ResourceBase OnBeforeInitialize(Func<ITestResource, Task> hook)
    {
      _beforeInitHooks.Add(hook ?? throw new ArgumentNullException(nameof(hook)));
      return this;
    }

    /// <summary>
    /// Adds a hook invoked after the resource is ready.
    /// </summary>
    public ResourceBase OnAfterReady(Func<ITestResource, Task> hook)
    {
      _afterReadyHooks.Add(hook ?? throw new ArgumentNullException(nameof(hook)));
      return this;
    }

    /// <summary>
    /// Adds a hook invoked before disposal starts.
    /// </summary>
    public ResourceBase OnBeforeDispose(Func<ITestResource, Task> hook)
    {
      _beforeDisposeHooks.Add(hook ?? throw new ArgumentNullException(nameof(hook)));
      return this;
    }

    /// <summary>
    /// Adds a hook invoked after disposal completes.
    /// </summary>
    public ResourceBase OnAfterDispose(Func<ITestResource, Task> hook)
    {
      _afterDisposeHooks.Add(hook ?? throw new ArgumentNullException(nameof(hook)));
      return this;
    }

    #endregion

    #region ITestResource

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
      await _lifecycleLock.WaitAsync(cancellationToken);
      try
      {
        if (IsInitialized)
          return;

        DriverId = ResolveDriverId();
        ValidateExpectedDriverType();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Options.InitializationTimeout);

        try
        {
          await RunHooksAsync(_beforeInitHooks, cts.Token);
          await PreflightAsync(cts.Token);
          _provisioned = true;
          await ProvisionAsync(cts.Token);
          Diagnostics = null;
          IsInitialized = true;
          await RunHooksAsync(_afterReadyHooks, cts.Token);
        }
        catch (Exception ex)
        {
          IsInitialized = false;
          try
          { Diagnostics = await CollectDiagnosticsAsync(ex, cts.Token); }
          catch { /* diagnostics must not mask the original failure */ }
          throw;
        }
      }
      finally
      {
        _lifecycleLock.Release();
      }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      await _lifecycleLock.WaitAsync();
      try
      {
        using var cts = new CancellationTokenSource(Options.TeardownTimeout);

        try
        {
          await RunHooksAsync(_beforeDisposeHooks, cts.Token);
        }
        catch
        {
          // Hooks should not prevent cleanup
        }

        Exception teardownFailure = null;

        if (_provisioned)
        {
          try
          {
            await TeardownAsync(cts.Token);
            _provisioned = false;
          }
          catch (Exception ex)
          {
            if (Options.ForceRemoveOnDispose)
            {
              using var forceCts = new CancellationTokenSource(Options.TeardownTimeout);
              try
              { await ForceRemoveAsync(forceCts.Token); }
              catch { /* best effort */ }
              _provisioned = false;
            }
            else
            {
              teardownFailure = ex;
              // _provisioned stays true so next DisposeAsync retries
            }
          }
        }

        IsInitialized = false;

        try
        {
          await RunHooksAsync(_afterDisposeHooks, cts.Token);
        }
        catch
        {
          // Hooks should not throw after cleanup
        }

        if (teardownFailure != null)
          ExceptionDispatchInfo.Capture(teardownFailure).Throw();
      }
      finally
      {
        _lifecycleLock.Release();
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
    /// <param name="cancellationToken">Cancellation token bound to
    /// <see cref="DockerResourceOptions.TeardownTimeout"/>.</param>
    protected abstract Task TeardownAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Force-removes the resource when graceful teardown fails.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token bound to
    /// <see cref="DockerResourceOptions.TeardownTimeout"/>.</param>
    protected abstract Task ForceRemoveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Collects diagnostic information when initialization fails.
    /// </summary>
    protected virtual Task<ResourceDiagnostics> CollectDiagnosticsAsync(
        Exception failure,
        CancellationToken cancellationToken = default)
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
      if (Options.Driver == null)
        throw new InvalidOperationException(
            "DockerResourceOptions.Driver is null. " +
            "Use DriverSelection.Default or DriverSelection.Specific(id) instead.");

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

    /// <summary>
    /// Validates that the resolved driver matches the expected type, if specified.
    /// </summary>
    private void ValidateExpectedDriverType()
    {
      if (Options.Driver.ExpectedType.HasValue)
      {
        var pack = Kernel.GetDriverPack(DriverId);
        if (pack.Type != Options.Driver.ExpectedType.Value)
          throw new InvalidOperationException(
              $"Expected driver type '{Options.Driver.ExpectedType.Value}' but " +
              $"driver '{DriverId}' is type '{pack.Type}'.");
      }
    }

    /// <summary>
    /// Truncates log output to <see cref="DockerResourceOptions.MaxDiagnosticLogLines"/>.
    /// </summary>
    protected string TruncateLogLines(string logs)
    {
      if (string.IsNullOrEmpty(logs) || Options.MaxDiagnosticLogLines <= 0)
        return logs;

      var lines = logs.Split('\n');
      if (lines.Length <= Options.MaxDiagnosticLogLines)
        return logs;

      return string.Join('\n', lines.Take(Options.MaxDiagnosticLogLines))
           + $"\n... ({lines.Length - Options.MaxDiagnosticLogLines} lines truncated)";
    }

    private async Task RunHooksAsync(
        List<Func<ITestResource, Task>> hooks,
        CancellationToken cancellationToken)
    {
      foreach (var hook in hooks)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var hookTask = hook(this);
        var completed = await Task.WhenAny(
            hookTask,
            Task.Delay(Timeout.Infinite, cancellationToken));

        if (completed != hookTask)
          cancellationToken.ThrowIfCancellationRequested();

        await hookTask;
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
