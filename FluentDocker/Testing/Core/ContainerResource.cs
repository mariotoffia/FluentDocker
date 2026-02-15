using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Services;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A single-container test resource with async lifecycle.
  /// </summary>
  public class ContainerResource : ResourceBase
  {
    private readonly Action<IContainerBuilder> _configure;

    /// <summary>
    /// Creates a container resource.
    /// </summary>
    /// <param name="kernel">Kernel with registered drivers.</param>
    /// <param name="configure">Container builder configuration callback.</param>
    /// <param name="options">Optional resource options.</param>
    public ContainerResource(
        FluentDockerKernel kernel,
        Action<IContainerBuilder> configure,
        DockerResourceOptions options = null)
        : base(kernel, options) => _configure = configure ?? throw new ArgumentNullException(nameof(configure));

    /// <summary>
    /// The running container service, available after initialization.
    /// </summary>
    public IContainerService Container { get; private set; }

    /// <summary>
    /// Inspects the container.
    /// </summary>
    public Task<Container> InspectAsync(CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      return Container.InspectAsync(cancellationToken);
    }

    /// <summary>
    /// Gets container logs.
    /// </summary>
    public Task<string> GetLogsAsync(CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      return Container.GetLogsAsync(false, cancellationToken);
    }

    /// <summary>
    /// Executes a command inside the container.
    /// </summary>
    public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      return Container.ExecuteAsync(command, cancellationToken);
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      await CapabilityChecks.EnsureContainerSupportAsync(Kernel, DriverId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var builder = new Builder();
      builder.WithinDriver(DriverId, Kernel);
      builder.UseContainer(_configure);

      var results = await builder.BuildAsync(cancellationToken);
      if (results.All.Count > 0 && results.All[0] is IContainerService container)
      {
        Container = container;
        ResourceName = container.Name ?? container.Id;
      }
      else
      {
        throw new InvalidOperationException("Builder did not produce a container service");
      }
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync()
    {
      if (Container == null)
        return;

      await Container.StopAsync();
      await Container.RemoveAsync(force: false);
      Container = null;
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync()
    {
      var c = Container;
      Container = null;
      if (c == null)
        return;

      try
      { await c.RemoveAsync(force: true); }
      catch { /* best effort */ }
    }

    /// <inheritdoc />
    protected override async Task<ResourceDiagnostics> CollectDiagnosticsAsync(Exception failure)
    {
      var diag = await base.CollectDiagnosticsAsync(failure);

      if (Container != null && Options.CaptureLogsOnFailure)
      {
        try
        {
          diag.Logs = TruncateLogLines(await Container.GetLogsAsync(false));
        }
        catch
        {
          diag.Logs = "(failed to collect logs)";
        }

        try
        {
          var info = await Container.InspectAsync();
          diag.InspectPayload = info?.ToString();
        }
        catch
        {
          diag.InspectPayload = "(failed to collect inspect data)";
        }
      }

      return diag;
    }

    #endregion

    private void EnsureInitialized()
    {
      if (!IsInitialized || Container == null)
        throw new InvalidOperationException(
            "Container resource is not initialized. Call InitializeAsync first.");
    }
  }
}
