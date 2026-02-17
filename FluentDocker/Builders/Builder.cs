using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Kernel;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  /// <summary>
  /// v3.0.0 async builder with WithinDriver() scoping and terminal BuildAsync().
  /// For type-safe driver-specific APIs, use <see cref="WithinDockerCli"/>,
  /// <see cref="WithinDockerApi"/>, or <see cref="WithinPodmanCli"/>.
  /// </summary>
  public class Builder : IBuilder
  {
    private FluentDockerKernel _currentKernel;
    private string _currentDriverId;
    private readonly List<BuildOperation> _operations = new();

    /// <summary>
    /// Creates a new builder.
    /// </summary>
    public Builder()
    {
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
      return new DockerCliFluentBuilder(this);
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
      return new DockerApiFluentBuilder(this);
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
      return new PodmanCliFluentBuilder(this);
    }

    #endregion

    #region Common Operations

    /// <summary>
    /// Adds a container operation to the current scope.
    /// </summary>
    public Builder UseContainer(Action<IContainerBuilder> configure)
    {
      ValidateScope();
      var builder = new ContainerBuilder(_currentKernel, _currentDriverId);
      configure(builder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ExecuteAsync = ct => builder.ExecuteAsync(ct),
        PostStartAsync = ct => builder.ExecuteDeferredWaitConditionsAsync(ct)
      });
      return this;
    }

    /// <summary>
    /// Adds a network operation to the current scope.
    /// </summary>
    public Builder UseNetwork(Action<INetworkBuilder> configure)
    {
      ValidateScope();
      var builder = new NetworkBuilder(_currentKernel, _currentDriverId);
      configure(builder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ExecuteAsync = ct => builder.ExecuteAsync(ct)
      });
      return this;
    }

    /// <summary>
    /// Adds a volume operation to the current scope.
    /// </summary>
    public Builder UseVolume(Action<IVolumeBuilder> configure)
    {
      ValidateScope();
      var builder = new VolumeBuilder(_currentKernel, _currentDriverId);
      configure(builder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ExecuteAsync = ct => builder.ExecuteAsync(ct)
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
      ValidateScope();
      var builder = new ComposeBuilder(_currentKernel, _currentDriverId);
      configure(builder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ExecuteAsync = ct => builder.ExecuteAsync(ct)
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
      ValidateScope();
      var builder = new PodBuilder(_currentKernel, _currentDriverId);
      configure(builder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ExecuteAsync = ct => builder.ExecuteAsync(ct)
      });
      return this;
    }

    /// <summary>
    /// Adds an image build operation to the current scope.
    /// </summary>
    public Builder UseImage(string imageName, Action<DockerfileBuilder> configure)
    {
      ValidateScope();
      var imageBuilder = new ImageBuilder(_currentKernel, _currentDriverId, imageName);
      var dockerfileBuilder = imageBuilder.From();
      configure(dockerfileBuilder);
      _operations.Add(new BuildOperation
      {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ExecuteAsync = ct => imageBuilder.ExecuteAsync(ct)
            .ContinueWith(t => (IService)t.Result, ct)
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
      var effectiveCleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(120);
      var scopes = new Dictionary<(FluentDockerKernel, string), BuildScope>();
      var groupedOps = _operations.GroupBy(op => (op.Kernel, op.DriverId));

      try
      {
        foreach (var group in groupedOps)
        {
          var key = group.Key;
          var scope = new BuildScope(key.Kernel, key.DriverId);
          scopes[key] = scope;

          var groupOperations = group.ToList();
          foreach (var operation in groupOperations)
          {
            var service = await operation.ExecuteAsync(cancellationToken);
            scope.AddResult(service);
          }

          await StartContainersWithLinksAsync(scope, cancellationToken);

          // Execute deferred wait conditions for linked containers
          foreach (var operation in groupOperations)
          {
            if (operation.PostStartAsync != null)
              await operation.PostStartAsync(cancellationToken);
          }
        }
      }
      catch
      {
        // Clean up all services created so far to prevent resource leaks.
        // Use a bounded timeout so cleanup cannot hang indefinitely when the daemon is unhealthy.
        using var cleanupCts = new CancellationTokenSource(effectiveCleanupTimeout);
        foreach (var scope in scopes.Values)
          await scope.DisposeAllAsync(cleanupCts.Token);
        throw;
      }

      return new BuildResults(scopes.Values.ToList());
    }

    #endregion

    #region Private

    private void SetScope(string driverId, FluentDockerKernel kernel)
    {
      _currentKernel = kernel ?? _currentKernel;

      if (_currentKernel == null)
      {
        throw new InvalidOperationException(
            "Kernel required in first WithinDriver() call. " +
            "Provide a kernel or create one with FluentDockerKernel.Create().BuildAsync()");
      }

      _currentDriverId = driverId;
    }

    private async Task StartContainersWithLinksAsync(
        BuildScope scope, CancellationToken cancellationToken)
    {
      var containersToStart = scope.Results
          .OfType<IContainerService>()
          .Where(c => c.State != ServiceRunningState.Running)
          .ToList();

      if (containersToStart.Count == 0)
        return;

      var driver = scope.Kernel.SysCtl<Drivers.IContainerDriver>(scope.DriverId);
      var context = new DriverContext(scope.DriverId);

      foreach (var container in containersToStart)
      {
        await container.StartAsync(cancellationToken);
        await WaitForContainerRunningAsync(driver, context, container.Id, cancellationToken);
      }
    }

    private async Task WaitForContainerRunningAsync(
        Drivers.IContainerDriver driver, DriverContext context,
        string containerId, CancellationToken cancellationToken)
    {
      const int maxAttempts = 30;
      const int delayMs = 100;
      for (var i = 0; i < maxAttempts; i++)
      {
        var inspectResult = await driver.InspectAsync(
            context, containerId, cancellationToken);
        if (inspectResult.Success && inspectResult.Data?.State?.Running == true)
          return;
        await Task.Delay(delayMs, cancellationToken);
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

  /// <summary>
  /// Represents a build operation to be executed.
  /// </summary>
  internal class BuildOperation
  {
    public FluentDockerKernel Kernel { get; set; }
    public string DriverId { get; set; }
    public Func<CancellationToken, Task<IService>> ExecuteAsync { get; set; }

    /// <summary>
    /// Optional post-start callback for executing deferred operations
    /// (e.g., wait conditions on linked containers).
    /// </summary>
    public Func<CancellationToken, Task> PostStartAsync { get; set; }
  }

  /// <summary>
  /// Interface for the v3.0.0 fluent builder.
  /// Exposes only operations common to all drivers. For driver-specific
  /// operations, use the typed builder returned by
  /// <see cref="Builder.WithinDockerCli"/>, <see cref="Builder.WithinDockerApi"/>,
  /// or <see cref="Builder.WithinPodmanCli"/>.
  /// </summary>
  public interface IBuilder
  {
    Builder WithinDriver(string driverId, FluentDockerKernel kernel = null);
    Builder UseContainer(Action<IContainerBuilder> configure);
    Builder UseNetwork(Action<INetworkBuilder> configure);
    Builder UseVolume(Action<IVolumeBuilder> configure);

    /// <summary>
    /// Adds an image build operation.
    /// </summary>
    Builder UseImage(string imageName, Action<DockerfileBuilder> configure);

    /// <summary>
    /// Builds all operations synchronously (TERMINAL operation).
    /// For async contexts, prefer BuildAsync() to avoid deadlocks.
    /// </summary>
    BuildResults Build();

    /// <summary>
    /// Builds all operations asynchronously (TERMINAL operation).
    /// </summary>
    /// <param name="cleanupTimeout">
    /// Maximum time allowed for cleanup on build failure.
    /// Defaults to 120 seconds.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the build.</param>
    Task<BuildResults> BuildAsync(
        TimeSpan? cleanupTimeout = null,
        CancellationToken cancellationToken = default);
  }
}
