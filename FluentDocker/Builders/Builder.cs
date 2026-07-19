using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Builders
{
  /// <summary>
  /// v3.0.0 async builder with WithinDriver() scoping and terminal BuildAsync().
  /// For type-safe driver-specific APIs, use <see cref="WithinDockerCli"/>,
  /// <see cref="WithinDockerApi"/>, or <see cref="WithinPodmanCli"/>.
  /// </summary>
  /// <remarks>
  /// A <see cref="Builder"/> instance is not thread-safe for configuration: only concurrent
  /// <see cref="BuildAsync(TimeSpan?, CancellationToken)"/> calls are guarded; mutate operations
  /// from a single thread before building.
  /// </remarks>
  public partial class Builder : IBuilder, IDriverScopedBuilder
  {
    private FluentDockerKernel _currentKernel = null!;
    private string _currentDriverId = null!;
    private readonly List<BuildOperation> _operations = [];
    private const string ModelBuilderAfterOpsMessage =
        "UseModelRunner()/UseModel() cannot be chained after UseContainer/UseNetwork/UseVolume/UseImage/UseCompose/UsePod operations; the model builders return directly and are not part of the deferred build pipeline. Call UseModelRunner()/UseModel() on a fresh Builder.";
    internal IEnumerable<object> ResourceBuilders =>
        _operations.Where(o => o.ResourceBuilder != null).Select(o => o.ResourceBuilder!);
    private volatile bool _buildSucceeded;
    private int _buildInProgress;

    /// <summary>
    /// Creates a new builder.
    /// </summary>
    public Builder()
    {
    }

    FluentDockerKernel IDriverScopedBuilder.Kernel
    {
      get
      {
        ValidateScope();
        return _currentKernel;
      }
    }

    string IDriverScopedBuilder.DriverId
    {
      get
      {
        ValidateScope();
        return _currentDriverId;
      }
    }

    #region Driver Scoping

    /// <summary>
    /// Establishes a driver scope for subsequent operations (generic, any driver).
    /// Prefer <see cref="WithinDockerCli"/>, <see cref="WithinDockerApi"/>,
    /// or <see cref="WithinPodmanCli"/> for type-safe access to driver-specific features.
    /// </summary>
    /// <param name="driverId">Driver identifier</param>
    /// <param name="kernel">Kernel instance (reuses previous if null)</param>
    public Builder WithinDriver(string driverId, FluentDockerKernel? kernel = null)
    {
      SetScope(driverId, kernel);
      return this;
    }

    /// <summary>
    /// Establishes a Docker CLI driver scope with type-safe access to Compose.
    /// </summary>
    /// <param name="driverId">Driver identifier</param>
    /// <param name="kernel">Kernel instance (reuses previous if null)</param>
    public DockerCliFluentBuilder WithinDockerCli(
        string driverId, FluentDockerKernel? kernel = null)
    {
      SetScope(driverId, kernel);
      // Deliberately no IComposeDriver probe here: containers/networks/volumes/images do not
      // need compose, so a compose-less Docker-CLI-shaped pack may still use the typed builder.
      // UseCompose performs the capability check at the fluent call instead (BF-13).
      return new DockerCliFluentBuilder(this, _currentKernel, _currentDriverId);
    }

    /// <summary>
    /// Establishes a Docker API driver scope.
    /// </summary>
    /// <param name="driverId">Driver identifier</param>
    /// <param name="kernel">Kernel instance (reuses previous if null)</param>
    public DockerApiFluentBuilder WithinDockerApi(
        string driverId, FluentDockerKernel? kernel = null)
    {
      SetScope(driverId, kernel);
      return new DockerApiFluentBuilder(this, _currentKernel, _currentDriverId);
    }

    /// <summary>
    /// Establishes a Podman CLI driver scope with type-safe access to pods.
    /// </summary>
    /// <param name="driverId">Driver identifier</param>
    /// <param name="kernel">Kernel instance (reuses previous if null)</param>
    public PodmanCliFluentBuilder WithinPodmanCli(
        string driverId, FluentDockerKernel? kernel = null)
    {
      SetScope(driverId, kernel);
      // Fail fast when the scoped driver is not actually Podman-CLI-capable: otherwise a later
      // UsePod queues happily and fails deep inside BuildAsync (BLD-MAJ-6).
      RequireScopedPort<Drivers.Podman.IPodmanPodDriver>();
      return new PodmanCliFluentBuilder(this, _currentKernel, _currentDriverId);
    }

    #endregion

    #region Common Operations

    /// <summary>
    /// Adds a container operation to the current scope.
    /// </summary>
    public Builder UseContainer(Action<IContainerBuilder> configure)
    {
      ArgumentNullException.ThrowIfNull(configure);
      ValidateScope();
      var builder = new ContainerBuilder(_currentKernel, _currentDriverId);
      configure(builder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ResourceBuilder = builder,
        ResourceKind = "container",
        ResourceName = builder.ContainerName,
        NetworkReferences = builder.NetworkReferences,
        VolumeReferences = builder.VolumeReferences,
        LinkReferences = builder.LinkReferences,
        ImageReferences = builder.ImageReferences,
        PodReferences = builder.PodReferences,
        ExecuteAsync = (cleanupTimeout, ct) => builder.ExecuteAsync(cleanupTimeout, ct),
        GetFailedService = () => builder.PendingService,
        PostStartAsync = ct => builder.ExecuteDeferredWaitConditionsAsync(ct),
        ResetForRetry = builder.ResetForRetry,
        FailureKeepReason = builder.FailureKeepReason,
        AllowCleanExit = builder.AllowCleanExitOnStart,
        StartDeferred = () => builder.StartDeferred,
        StartupTimeoutMs = builder.StartupTimeoutMs,
        StartupPollIntervalMs = builder.StartupPollIntervalMs
      });
      return this;
    }

    /// <summary>
    /// Adds a network operation to the current scope.
    /// </summary>
    public Builder UseNetwork(Action<INetworkBuilder> configure)
    {
      ArgumentNullException.ThrowIfNull(configure);
      ValidateScope();
      var builder = new NetworkBuilder(_currentKernel, _currentDriverId);
      configure(builder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ResourceBuilder = builder,
        ResourceKind = "network",
        ResourceName = builder.Name,
        ExecuteAsync = (_, ct) => builder.ExecuteAsync(ct),
        ForceRemoveOnFailure = _ => builder.CreatedResource,
        FailureKeepReason = _ => builder.CreatedResource ? null : "borrowed"
      });
      return this;
    }

    /// <summary>
    /// Adds a volume operation to the current scope.
    /// </summary>
    public Builder UseVolume(Action<IVolumeBuilder> configure)
    {
      ArgumentNullException.ThrowIfNull(configure);
      ValidateScope();
      var builder = new VolumeBuilder(_currentKernel, _currentDriverId);
      configure(builder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ResourceBuilder = builder,
        ResourceKind = "volume",
        ResourceName = builder.Name,
        ExecuteAsync = (_, ct) => builder.ExecuteAsync(ct),
        ForceRemoveOnFailure = _ => builder.CreatedResource,
        FailureKeepReason = _ => builder.CreatedResource ? null : "borrowed"
      });
      return this;
    }

    /// <summary>
    /// Adds a compose operation. Requires the Docker CLI driver.
    /// Prefer using <see cref="DockerCliFluentBuilder.UseCompose"/> via
    /// <see cref="WithinDockerCli"/> for type-safe access.
    /// </summary>
    /// <exception cref="Common.InterfaceNotSupportedException">
    /// The scoped driver does not support compose (<see cref="Drivers.IComposeDriver"/>).
    /// </exception>
    public Builder UseCompose(Action<IComposeBuilder> configure)
    {
      ArgumentNullException.ThrowIfNull(configure);
      ValidateScope();
      // Fail fast when the scoped driver is not compose-capable: otherwise the operation queues
      // happily and fails deep inside BuildAsync, possibly after other resources were created.
      RequireScopedPort<Drivers.IComposeDriver>();
      var builder = new ComposeBuilder(_currentKernel, _currentDriverId);
      configure(builder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ResourceBuilder = builder,
        ResourceKind = "compose",
        ResourceName = builder.ProjectName,
        ExecuteAsync = (cleanupTimeout, ct) => builder.ExecuteAsync(cleanupTimeout, ct),
        GetFailedService = () => builder.PendingService,
        ResetForRetry = builder.ResetForRetry,
        ForceRemoveOnFailure = builder.ForceRemoveOnFailure,
        FailureKeepReason = builder.FailureKeepReason
      });
      return this;
    }

    /// <summary>
    /// Adds a pod operation. Requires the Podman CLI driver.
    /// Prefer using <see cref="PodmanCliFluentBuilder.UsePod"/> via
    /// <see cref="WithinPodmanCli"/> for type-safe access.
    /// </summary>
    public Builder UsePod(Action<IPodBuilder> configure)
    {
      ArgumentNullException.ThrowIfNull(configure);
      ValidateScope();
      // Fail fast when the scoped driver is not pod-capable (mirrors UseCompose).
      RequireScopedPort<Drivers.Podman.IPodmanPodDriver>();
      var builder = new PodBuilder(_currentKernel, _currentDriverId);
      configure(builder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ResourceKind = "pod",
        ResourceName = builder.PodName,
        ExecuteAsync = (_, ct) => builder.ExecuteAsync(ct),
        GetFailedService = () => builder.PendingService,
        ResetForRetry = builder.ResetForRetry,
        ForceRemoveOnFailure = _ => builder.CreatedResource
      });
      return this;
    }

    /// <summary>
    /// Adds an image build operation to the current scope.
    /// </summary>
    public Builder UseImage(string imageName, Action<DockerfileBuilder> configure)
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(imageName);
      ArgumentNullException.ThrowIfNull(configure);
      ValidateScope();
      var imageBuilder = new ImageBuilder(_currentKernel, _currentDriverId, imageName);
      var dockerfileBuilder = imageBuilder.From();
      configure(dockerfileBuilder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ResourceKind = "image",
        ResourceName = imageName,
        ExecuteAsync = async (_, ct) => (IServiceAsync)await imageBuilder.ExecuteAsync(ct).ConfigureAwait(false),
        FailureKeepReason = _ => "built"
      });
      return this;
    }

    #endregion

    #region Terminal

    /// <summary>
    /// TERMINAL - Builds all operations synchronously.
    /// </summary>
    public BuildResults Build()
    {
      return Task.Run(() => BuildAsync()).GetAwaiter().GetResult();
    }

    /// <summary>
    /// TERMINAL - Builds all operations asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the build.</param>
    /// <param name="cleanupTimeout">
    /// Maximum time allowed for cleanup on build failure. Must be non-negative; a negative value
    /// throws <see cref="ArgumentOutOfRangeException"/>. Defaults to 120 seconds when null.
    /// </param>
    public async Task<BuildResults> BuildAsync(
        TimeSpan? cleanupTimeout = null,
        CancellationToken cancellationToken = default)
    {
      // Reject a negative cleanup timeout up front (before the latch); it would otherwise detonate in the failure path.
      ArgumentOutOfRangeException.ThrowIfLessThan(cleanupTimeout.GetValueOrDefault(), TimeSpan.Zero);
      if (_buildSucceeded)
        throw new InvalidOperationException("builder already consumed by BuildAsync; create a new Builder");

      // Acquire the in-progress latch FIRST, then run pre-flight under it. RefreshContainerSnapshots
      // WRITES operation.ResourceName/reference/wait fields (it is NOT read-only), so running it
      // before the latch let a second concurrent BuildAsync mutate fields the in-flight build reads
      // (BLD-MAJ-2). A pre-flight failure releases the latch in the catch so a validation error
      // cannot brick every subsequent BuildAsync or skip the per-operation ResetForRetry.
      if (Interlocked.CompareExchange(ref _buildInProgress, 1, 0) != 0)
        throw new InvalidOperationException("BuildAsync is already running on this Builder instance");

      try
      {
        if (_buildSucceeded)
          throw new InvalidOperationException("builder already consumed by BuildAsync; create a new Builder");
        if (_operations.Count == 0)
          throw new InvalidOperationException("no resources configured");
        // Re-snapshot container refs/wait params from the live builders so validation and deferred
        // start see exactly what ExecuteAsync will use. Guards the foot-gun where a stashed
        // IContainerBuilder is mutated after its configure lambda returns.
        RefreshContainerSnapshots();
        ValidateContiguousScopes();
        ValidateOperationReferences();
      }
      catch
      {
        Interlocked.Exchange(ref _buildInProgress, 0);
        throw;
      }

      var effectiveCleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(120);
      // ponytail: a List (not a Dictionary) so BuildResults' reverse-disposal order (dependents
      // before dependencies, e.g. containers before their network) is guaranteed by insertion
      // order, not by Dictionary<K,V> enumeration (an unspecified CLR detail). GroupBy below
      // already yields unique (Kernel, DriverId) keys, so no keyed lookup is needed here.
      var scopes = new List<BuildScope>();
      var groupedOps = _operations.GroupBy(op => (op.Kernel, op.DriverId));
      var completedOperations = new List<(BuildOperation Operation, IServiceAsync Service)>();

      // Retry contract: every build attempt centrally resets per-operation attempt state.
      foreach (var operation in _operations)
        operation.ResetForRetry?.Invoke();

      try
      {
        foreach (var group in groupedOps)
        {
          var key = group.Key;
          var scope = new BuildScope(key.Kernel, key.DriverId);
          scopes.Add(scope);

          var groupOperations = group.ToList();
          var executedOperations = new List<(BuildOperation Operation, IServiceAsync Service)>();
          foreach (var operation in groupOperations)
          {
            IServiceAsync service;
            try
            {
              service = await operation.ExecuteAsync(effectiveCleanupTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
              var failedService = operation.GetFailedService?.Invoke();
              if (failedService != null)
                completedOperations.Add((operation, failedService));
              throw;
            }
            scope.AddResult(service);
            if (service != null)
            {
              executedOperations.Add((operation, service));
              completedOperations.Add((operation, service));
            }
          }

          await StartContainersWithLinksAsync(scope, executedOperations, cancellationToken).ConfigureAwait(false);

          // Execute deferred wait conditions for linked containers
          foreach (var operation in groupOperations)
          {
            if (operation.PostStartAsync != null)
              await operation.PostStartAsync(cancellationToken).ConfigureAwait(false);
          }
        }

        _buildSucceeded = true;
        return new BuildResults(scopes);
      }
      catch (Exception ex)
      {
        (_currentKernel?.LoggerFactory ?? NullLoggerFactory.Instance)
            .CreateLogger<Builder>()
            .LogError(ex, "Builder build failed");
        // Clean up all services created so far to prevent resource leaks.
        // Use a bounded timeout so cleanup cannot hang indefinitely when the daemon is unhealthy.
        var manifest = await CleanupFailedBuildAsync(completedOperations, effectiveCleanupTimeout).ConfigureAwait(false);
        ex.Data[BuildFailureManifest.BuildFailureManifestKey] = manifest;
        throw;
      }
      finally
      {
        Volatile.Write(ref _buildInProgress, 0);
      }
    }

    #endregion

    #region Private

    private void SetScope(string driverId, FluentDockerKernel? kernel)
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
      _currentKernel = kernel ?? _currentKernel ?? throw new InvalidOperationException(
          "Kernel required in first WithinDriver() call. " +
          "Provide a kernel or create one with FluentDockerKernel.Create().BuildAsync()");

      _currentDriverId = driverId;
    }

    /// <summary>
    /// Throws <see cref="Common.InterfaceNotSupportedException"/> when the currently scoped driver
    /// cannot resolve the port <typeparamref name="T"/> — used by the typed <c>WithinXxx</c> wrappers
    /// to reject a wrong-kind driver up front instead of deep inside <c>BuildAsync</c>.
    /// </summary>
    private void RequireScopedPort<T>() where T : class
    {
      if (_currentKernel == null || !_currentKernel.TrySysCtl<T>(_currentDriverId, out _))
        throw new Common.InterfaceNotSupportedException(_currentDriverId, typeof(T).Name);
    }

    internal T RunInScope<T>(FluentDockerKernel kernel, string driverId, Func<T> action)
    {
      var previousKernel = _currentKernel;
      var previousDriverId = _currentDriverId;
      SetScope(driverId, kernel);
      try
      {
        return action();
      }
      finally
      {
        _currentKernel = previousKernel;
        _currentDriverId = previousDriverId;
      }
    }

    internal void RunInScope(FluentDockerKernel kernel, string driverId, Action action) =>
        RunInScope(kernel, driverId, () => { action(); return true; });

    private static async Task StartContainersWithLinksAsync(
        BuildScope scope,
        IReadOnlyList<(BuildOperation Operation, IServiceAsync Service)> operations,
        CancellationToken cancellationToken)
    {
      // Pair with the operation captured when it produced a non-null result; BuildScope.Results intentionally skips nulls.
      var containersToStart = operations
          .Where(x => x.Operation.StartDeferred() && x.Service is IContainerService)
          .ToList();

      if (containersToStart.Count == 0)
        return;

      var driver = scope.Kernel.SysCtl<Drivers.IContainerDriver>(scope.DriverId);
      var context = new DriverContext(scope.DriverId);

      foreach (var item in containersToStart)
      {
        var container = (IContainerService)item.Service;
        await container.StartAsync(cancellationToken).ConfigureAwait(false);
        await ContainerBuilder.WaitForContainerStartedAsync(
            driver, context, container.Id, item.Operation.ResourceName, item.Operation.AllowCleanExit,
            item.Operation.StartupTimeoutMs, item.Operation.StartupPollIntervalMs, cancellationToken)
            .ConfigureAwait(false);
      }
    }

    private void ValidateScope()
    {
      if (_currentKernel == null || _currentDriverId == null)
      {
        throw new InvalidOperationException(
            "Must call WithinDriver() before adding operations");
      }
    }

    #endregion
  }

}
