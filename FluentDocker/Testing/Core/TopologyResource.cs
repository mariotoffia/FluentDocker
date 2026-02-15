using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Services;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A multi-container topology with optional shared networks and volumes.
  /// Containers are started in declaration order and torn down in reverse order.
  /// </summary>
  public class TopologyResource : ResourceBase
  {
    private readonly Action<Builder> _configure;
    private readonly List<IServiceAsync> _services = new();

    /// <summary>
    /// Creates a topology resource.
    /// </summary>
    /// <param name="kernel">Kernel with registered drivers.</param>
    /// <param name="configure">Builder configuration for the full topology.</param>
    /// <param name="options">Optional resource options.</param>
    public TopologyResource(
        FluentDockerKernel kernel,
        Action<Builder> configure,
        DockerResourceOptions options = null)
        : base(kernel, options) => _configure = configure ?? throw new ArgumentNullException(nameof(configure));

    /// <summary>
    /// All services created by the topology, in build order.
    /// </summary>
    public IReadOnlyList<IServiceAsync> Services => _services;

    /// <summary>
    /// Gets a container service by name.
    /// </summary>
    public IContainerService GetContainer(string name)
    {
      return _services.OfType<IContainerService>()
          .FirstOrDefault(c => c.Name == name);
    }

    /// <summary>
    /// Gets a network service by name.
    /// </summary>
    public INetworkService GetNetwork(string name)
    {
      return _services.OfType<INetworkService>()
          .FirstOrDefault(n => n.NetworkName == name || n.Name == name);
    }

    /// <summary>
    /// Gets all container services.
    /// </summary>
    public IReadOnlyList<IContainerService> Containers =>
        _services.OfType<IContainerService>().ToList();

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      // Topology requires at least container support.
      await CapabilityChecks.EnsureContainerSupportAsync(Kernel, DriverId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var builder = new Builder();
      builder.WithinDriver(DriverId, Kernel);
      _configure(builder);

      var results = await builder.BuildAsync(cancellationToken);
      foreach (var service in results.All.OfType<IServiceAsync>())
      {
        _services.Add(service);
      }

      ResourceName = $"topology-{_services.Count}-services";
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync()
    {
      // Tear down in reverse order for dependency safety.
      for (var i = _services.Count - 1; i >= 0; i--)
      {
        try
        { await _services[i].StopAsync(); }
        catch { /* stop failure must not prevent removal */ }

        try
        { await _services[i].RemoveAsync(force: false); }
        catch { /* continue cleaning up remaining services */ }
      }

      _services.Clear();
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync()
    {
      for (var i = _services.Count - 1; i >= 0; i--)
      {
        try
        { await _services[i].RemoveAsync(force: true); }
        catch { /* best effort */ }
      }

      _services.Clear();
    }

    /// <inheritdoc />
    protected override async Task<ResourceDiagnostics> CollectDiagnosticsAsync(Exception failure)
    {
      var diag = await base.CollectDiagnosticsAsync(failure);

      if (Options.CaptureLogsOnFailure)
      {
        var logs = new List<string>();
        foreach (var container in _services.OfType<IContainerService>())
        {
          try
          {
            var log = await container.GetLogsAsync(false);
            logs.Add($"--- {container.Name ?? container.Id} ---\n{log}");
          }
          catch
          {
            logs.Add($"--- {container.Name ?? container.Id} --- (failed to collect)");
          }
        }

        diag.Logs = TruncateLogLines(string.Join("\n\n", logs));
      }

      return diag;
    }

    #endregion
  }
}
