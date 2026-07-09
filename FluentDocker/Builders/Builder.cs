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
    private FluentDockerKernel _currentKernel;
    private string _currentDriverId;
    private readonly List<BuildOperation> _operations = [];
    private const string ModelBuilderAfterOpsMessage =
        "UseModelRunner()/UseModel() cannot be chained after UseContainer/UseNetwork/UseVolume/UseImage/UseCompose/UsePod operations; the model builders return directly and are not part of the deferred build pipeline. Call UseModelRunner()/UseModel() on a fresh Builder.";
    internal IEnumerable<object> ResourceBuilders =>
        _operations.Where(o => o.ResourceBuilder != null).Select(o => o.ResourceBuilder);
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
    public Builder WithinDriver(string driverId, FluentDockerKernel kernel = null)
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
        string driverId, FluentDockerKernel kernel = null)
    {
      SetScope(driverId, kernel);
      return new DockerCliFluentBuilder(this, _currentKernel, _currentDriverId);
    }

    /// <summary>
    /// Establishes a Docker API driver scope.
    /// </summary>
    /// <param name="driverId">Driver identifier</param>
    /// <param name="kernel">Kernel instance (reuses previous if null)</param>
    public DockerApiFluentBuilder WithinDockerApi(
        string driverId, FluentDockerKernel kernel = null)
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
        string driverId, FluentDockerKernel kernel = null)
    {
      SetScope(driverId, kernel);
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
    /// Begins building an <see cref="Services.IModelRunner"/> in the current scope
    /// (set by <see cref="WithinDriver(string, FluentDockerKernel)"/>). Unlike the
    /// other <c>UseXxx</c> operations this returns the runner builder directly
    /// (the runner is not part of the deferred build pipeline).
    /// </summary>
    /// <returns>A model runner builder.</returns>
    public IModelRunnerBuilder UseModelRunner()
    {
      ValidateScope();
      if (_operations.Count > 0)
        throw new InvalidOperationException(ModelBuilderAfterOpsMessage);
      var builder = new ModelRunnerBuilder(_currentKernel, _currentDriverId);
      // Shared fail-fast capability guard (same one the driver-scoped extensions use).
      if (!ModelDriverScopedBuilderExtensions.HasAnyModelPort(builder))
        throw new Common.InterfaceNotSupportedException(_currentDriverId, nameof(IModelRunnerBuilder));
      return builder;
    }

    /// <summary>
    /// Begins building a managed single-model <see cref="Services.IModelService"/> in
    /// the current scope.
    /// </summary>
    /// <param name="reference">The model reference string.</param>
    /// <returns>A model service builder.</returns>
    public IModelServiceBuilder UseModel(string reference) =>
        UseModel(Model.Models.ModelReference.Parse(reference));

    /// <summary>
    /// Begins building a managed single-model <see cref="Services.IModelService"/> in
    /// the current scope from a pre-built <see cref="Model.Models.ModelReference"/>.
    /// </summary>
    /// <param name="reference">The model reference.</param>
    /// <returns>A model service builder.</returns>
    public IModelServiceBuilder UseModel(Model.Models.ModelReference reference)
    {
      ValidateScope();
      if (_operations.Count > 0)
        throw new InvalidOperationException(ModelBuilderAfterOpsMessage);
      var serviceBuilder = new ModelServiceBuilder(_currentKernel, _currentDriverId);
      if (!ModelDriverScopedBuilderExtensions.HasModelRuntime(serviceBuilder))
        throw new Common.InterfaceNotSupportedException(_currentDriverId, nameof(Drivers.IModelRuntimeDriver));
      return serviceBuilder.ForModel(reference);
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
    public Builder UseCompose(Action<IComposeBuilder> configure)
    {
      ArgumentNullException.ThrowIfNull(configure);
      ValidateScope();
      var builder = new ComposeBuilder(_currentKernel, _currentDriverId);
      configure(builder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ExecuteAsync = (cleanupTimeout, ct) => builder.ExecuteAsync(cleanupTimeout, ct),
        ForceRemoveOnFailure = _ => !builder.BorrowedProject,
        FailureKeepReason = _ => builder.BorrowedProject ? "borrowed" : null
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
    /// Maximum time allowed for cleanup on build failure.
    /// Defaults to 120 seconds.
    /// </param>
    public async Task<BuildResults> BuildAsync(
        TimeSpan? cleanupTimeout = null,
        CancellationToken cancellationToken = default)
    {
      if (_buildSucceeded)
        throw new InvalidOperationException("builder already consumed by BuildAsync; create a new Builder");
      // Pre-flight checks run BEFORE acquiring the in-progress latch so a validation failure
      // cannot leave the latch stuck set (which would brick every subsequent BuildAsync and
      // skip the per-operation ResetForRetry). Both checks are read-only.
      if (_operations.Count == 0)
        throw new InvalidOperationException("no resources configured");
      ValidateContiguousScopes();
      ValidateOperationReferences();

      if (Interlocked.CompareExchange(ref _buildInProgress, 1, 0) != 0)
        throw new InvalidOperationException("BuildAsync is already running on this Builder instance");
      if (_buildSucceeded)
      {
        Interlocked.Exchange(ref _buildInProgress, 0);
        throw new InvalidOperationException("builder already consumed by BuildAsync; create a new Builder");
      }

      var effectiveCleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(120);
      var scopes = new Dictionary<(FluentDockerKernel, string), BuildScope>();
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
          scopes[key] = scope;

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
        return new BuildResults([.. scopes.Values]);
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

    private void SetScope(string driverId, FluentDockerKernel kernel)
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
      _currentKernel = kernel ?? _currentKernel ?? throw new InvalidOperationException(
          "Kernel required in first WithinDriver() call. " +
          "Provide a kernel or create one with FluentDockerKernel.Create().BuildAsync()");

      _currentDriverId = driverId;
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
