using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using Microsoft.Extensions.Logging;
namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Base class for all Docker test resources. Provides shared lifecycle,
  /// diagnostics, cleanup, and hook infrastructure.
  /// </summary>
  public abstract partial class ResourceBase : ITestResource
  {
    private readonly List<Func<ITestResource, Task>> _beforeInitHooks = [];
    private readonly List<Func<ITestResource, Task>> _afterReadyHooks = [];
    private readonly List<Func<ITestResource, Task>> _beforeDisposeHooks = [];
    private readonly List<Func<ITestResource, Task>> _afterDisposeHooks = [];
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _provisionCommitLock = new();
    private bool _provisioned;
    private int _provisionGeneration;
    private int _disposeProvisionGeneration;
    private int _disposeStarted;
    private int _reaperRegistered;
    private Task? _abandonedProvision;
    // Records (driver, session) keys whose process-wide orphan sweep has already run (TST-MAJ-3).
    private static readonly ConcurrentDictionary<string, byte> _orphanSweepDone = new();

    /// <summary>
    /// Creates a new resource with the given kernel and options.
    /// </summary>
    protected ResourceBase(FluentDockerKernel kernel, DockerResourceOptions? options = null)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      Kernel = kernel;
      Options = options ?? new DockerResourceOptions();
      // Logger uses the *concrete* derived type as its category so users can filter per-resource.
      Logger = kernel.LoggerFactory.CreateLogger(GetType());
    }
    /// <summary>
    /// Logger for this resource. Category equals the concrete derived type's FQN.
    /// </summary>
    protected ILogger Logger { get; }
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
    // Non-null contract: assigned by ResolveDriverId during InitializeAsync before any
    // consumer read; consumer-facing accessors are guarded by initialization checks.
    public string DriverId { get; private set; } = null!;
    /// <summary>
    /// Unique name generated for this resource. Set during initialization.
    /// </summary>
    // Keep signature; public contract documents availability after initialization.
    public string ResourceName { get; protected set; } = null!;
    /// <summary>
    /// Diagnostics collected on failure.
    /// </summary>
    public ResourceDiagnostics? Diagnostics { get; private set; }

    /// <summary>
    /// Diagnostics captured when teardown fails during disposal.
    /// </summary>
    public TeardownDiagnostics? LastTeardownDiagnostics { get; private set; }

    #region Lifecycle Hooks

    /// <summary>
    /// Adds a hook invoked before initialization starts.
    /// </summary>
    public ResourceBase OnBeforeInitialize(Func<ITestResource, Task> hook)
    {
      ArgumentNullException.ThrowIfNull(hook);
      _beforeInitHooks.Add(hook);
      return this;
    }

    /// <summary>
    /// Adds a hook invoked after the resource is ready.
    /// </summary>
    public ResourceBase OnAfterReady(Func<ITestResource, Task> hook)
    {
      ArgumentNullException.ThrowIfNull(hook);
      _afterReadyHooks.Add(hook);
      return this;
    }

    /// <summary>
    /// Adds a hook invoked before disposal starts.
    /// </summary>
    public ResourceBase OnBeforeDispose(Func<ITestResource, Task> hook)
    {
      ArgumentNullException.ThrowIfNull(hook);
      _beforeDisposeHooks.Add(hook);
      return this;
    }

    /// <summary>
    /// Adds a hook invoked after disposal completes.
    /// </summary>
    public ResourceBase OnAfterDispose(Func<ITestResource, Task> hook)
    {
      ArgumentNullException.ThrowIfNull(hook);
      _afterDisposeHooks.Add(hook);
      return this;
    }

    #endregion

    #region ITestResource

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
      await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        if (IsInitialized)
          return;

        if (_provisioned)
          throw new InvalidOperationException(
              "Resource has been provisioned but is not initialized " +
              "(teardown may have failed). Call DisposeAsync to clean up " +
              "before re-initializing.");

        _disposeProvisionGeneration = 0;
        Interlocked.Exchange(ref _disposeStarted, 0);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Options.InitializationTimeout);

        try
        {
          DriverId = ResolveDriverId();
          if (ProcessExitReaper.Register(Kernel, DriverId, Options))
            Interlocked.Exchange(ref _reaperRegistered, 1);
          ValidateExpectedDriverType();
          await RunHooksAsync(_beforeInitHooks, cts.Token).ConfigureAwait(false);
          await EnsureRuntimeHealthyAsync(cts.Token).ConfigureAwait(false);
          await PreflightAsync(cts.Token).ConfigureAwait(false);

          // Orphan sweep is process-wide, not per-resource: on a 200-test suite with per-test
          // fixtures the O(tests) sweeps (3 list calls + inspects each) dominate. Run it once per
          // (driver, session) — the first resource that reaches it wins (TST-MAJ-3). Known
          // limitation (documented on CleanupOrphansOnInit): the key carries no endpoint
          // discriminator, so a second kernel using the same driver id against a DIFFERENT
          // daemon is not swept — keying per kernel would resurrect the O(tests) cost for
          // the per-test-kernel fixtures this exists to protect.
          if (Options.CleanupOrphansOnInit &&
              _orphanSweepDone.TryAdd($"{DriverId}\0{Options.SessionId}", 0))
          {
            try
            {
              var cleanup = await OrphanCleanup.CleanupOrphanedResourcesAsync(
                  Kernel, DriverId, Options.SessionId,
                  Options.OrphanCleanupMinimumAge, cts.Token).ConfigureAwait(false);
              LogOrphanCleanupErrors(cleanup);
            }
            catch (Exception ex) { LogOrphanCleanupFailure(ex); }
          }

          _provisioned = true;
          var provisionTask = ProvisionAsync(cts.Token);
          try
          {
            await provisionTask.WaitAsync(cts.Token).ConfigureAwait(false);
          }
          catch (OperationCanceledException ex)
              when (!cancellationToken.IsCancellationRequested && cts.IsCancellationRequested)
          {
            AbandonProvision(provisionTask);
            throw new TimeoutException(
                $"Resource initialization timed out after {Options.InitializationTimeout}.", ex);
          }
          catch (OperationCanceledException)
              when (cancellationToken.IsCancellationRequested)
          {
            AbandonProvision(provisionTask);
            throw;
          }
          Diagnostics = null;
          IsInitialized = true;
          await RunHooksAsync(_afterReadyHooks, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
            when (!cancellationToken.IsCancellationRequested && cts.IsCancellationRequested)
        {
          IsInitialized = false;
          if (!_provisioned)
            UnregisterReaper();
          var timeout = new TimeoutException(
              $"Resource initialization timed out after {Options.InitializationTimeout}.", ex);
          try
          {
            using var diagCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Diagnostics = await CollectDiagnosticsAsync(timeout, diagCts.Token).ConfigureAwait(false);
          }
          catch { /* diagnostics must not mask the original failure */ }
          throw CreateInitializationException(timeout);
        }
        catch (Exception ex)
        {
          IsInitialized = false;
          if (!_provisioned)
            UnregisterReaper();
          if (IsExternalCancellation(ex, cancellationToken))
            throw;

          try
          {
            // Fresh token — the init cts may already be canceled by
            // InitializationTimeout, which would abort diagnostics collection.
            using var diagCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Diagnostics = await CollectDiagnosticsAsync(ex, diagCts.Token).ConfigureAwait(false);
          }
          catch { /* diagnostics must not mask the original failure */ }
          throw CreateInitializationException(ex);
        }
      }
      finally
      {
        _lifecycleLock.Release();
      }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only the first caller runs teardown. A concurrent call made while that teardown
    /// is still in flight returns immediately; it does not await teardown or observe
    /// its result or exception. After a successful dispose the resource is terminal and
    /// further calls are no-ops; after a failed teardown the guard is released so a
    /// later call retries disposal. Await the first (or the retrying)
    /// <see cref="DisposeAsync"/> for the authoritative outcome, including
    /// <see cref="LastTeardownDiagnostics"/>.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposeStarted, 1, 0) != 0)
        return;

      using var cts = new CancellationTokenSource(Options.TeardownTimeout);
      var lockTaken = false;
      var disposalCompleted = false;
      try
      {
        try
        {
          await _lifecycleLock.WaitAsync(cts.Token).ConfigureAwait(false);
          lockTaken = true;
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
          throw new TimeoutException(
              $"Timed out waiting for resource lifecycle lock during disposal after {Options.TeardownTimeout}.", ex);
        }

        await WaitForAbandonedProvisionAsync(cts.Token).ConfigureAwait(false);

        try
        {
          await RunHooksAsync(_beforeDisposeHooks, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          BeforeDisposeHookFailed(Logger, ex);
        }

        Exception? teardownFailure = null;

        if (_provisioned)
        {
          Task? teardownTask = null;
          try
          {
            teardownTask = TeardownAsync(cts.Token);
            await teardownTask.WaitAsync(cts.Token).ConfigureAwait(false);
            _provisioned = false;
          }
          catch (Exception ex)
          {
            if (teardownTask != null)
              ObserveAbandonedCleanup(teardownTask);

            if (Options.ForceRemoveOnDispose)
            {
              Exception? forceRemoveFailure = null;
              using var forceCts = new CancellationTokenSource(Options.TeardownTimeout);
              Task? forceTask = null;
              try
              {
                forceTask = ForceRemoveAsync(forceCts.Token);
                await forceTask.WaitAsync(forceCts.Token).ConfigureAwait(false);
              }
              catch (Exception forceEx)
              {
                if (forceTask != null)
                  ObserveAbandonedCleanup(forceTask);
                forceRemoveFailure = forceEx;
              }
              LastTeardownDiagnostics = new TeardownDiagnostics
              {
                TeardownException = ex,
                ForceRemoveException = forceRemoveFailure
              };
              if (forceRemoveFailure != null)
              {
                GracefulAndForceRemoveFailed(Logger, ex);
                ForceRemoveFailed(Logger, forceRemoveFailure);
                teardownFailure = ex;
              }
              else
              {
                _provisioned = false;
                GracefulTeardownRecovered(Logger, ex);
              }
            }
            else
            {
              LastTeardownDiagnostics = new TeardownDiagnostics
              {
                TeardownException = ex
              };
              teardownFailure = ex;
              // _provisioned stays true so next DisposeAsync retries
            }
          }
        }

        IsInitialized = false;

        try
        {
          await RunHooksAsync(_afterDisposeHooks, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          AfterDisposeHookFailed(Logger, ex);
        }

        if (teardownFailure != null)
          ExceptionDispatchInfo.Capture(teardownFailure).Throw();

        UnregisterReaper();
        disposalCompleted = true;
      }
      finally
      {
        if (lockTaken)
          _lifecycleLock.Release();
        if (!disposalCompleted)
          Interlocked.Exchange(ref _disposeStarted, 0);
      }

      GC.SuppressFinalize(this);
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
        return Kernel.DefaultDriverId
               ?? throw new InvalidOperationException(
                   "Kernel has no default driver configured.");

      return Options.Driver.DriverId
             ?? throw new InvalidOperationException("DriverSelection has no DriverId set");
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

    private void UnregisterReaper()
    {
      if (Interlocked.Exchange(ref _reaperRegistered, 0) == 1)
        ProcessExitReaper.Unregister(Kernel, DriverId, Options.SessionId);
    }

    #endregion
  }

}
