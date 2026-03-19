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
        : base(kernel, options)
    {
      ArgumentNullException.ThrowIfNull(configure);
      _configure = configure;
    }

    /// <summary>
    /// All services created by the topology, in build order.
    /// </summary>
    public IReadOnlyList<IServiceAsync> Services
    {
      get { EnsureInitialized(); return _services.AsReadOnly(); }
    }

    /// <summary>
    /// Gets a container service by name.
    /// </summary>
    public IContainerService GetContainer(string name)
    {
      EnsureInitialized();
      return _services.OfType<IContainerService>()
          .FirstOrDefault(c => c.Name == name);
    }

    /// <summary>
    /// Gets a network service by name.
    /// </summary>
    public INetworkService GetNetwork(string name)
    {
      EnsureInitialized();
      return _services.OfType<INetworkService>()
          .FirstOrDefault(n => n.NetworkName == name || n.Name == name);
    }

    /// <summary>
    /// Gets all container services.
    /// </summary>
    public IReadOnlyList<IContainerService> Containers
    {
      get { EnsureInitialized(); return _services.OfType<IContainerService>().ToList(); }
    }

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

      var results = await builder.BuildAsync(
          cleanupTimeout: Options.TeardownTimeout,
          cancellationToken: cancellationToken);
      foreach (var service in results.All.OfType<IServiceAsync>())
      {
        _services.Add(service);
      }

      ResourceName = $"topology-{_services.Count}-services";
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      var failures = new List<Exception>();

      // Tear down in reverse order for dependency safety.
      for (var i = _services.Count - 1; i >= 0; i--)
      {
        try
        { await _services[i].StopAsync(cancellationToken); }
        catch { /* stop failure must not prevent removal */ }

        try
        { await _services[i].RemoveAsync(force: false, cancellationToken); }
        catch (Exception ex) { failures.Add(ex); }
      }

      if (failures.Count > 0)
        throw new AggregateException(
            $"{failures.Count} service(s) failed to remove during teardown.",
            failures);

      _services.Clear();
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      for (var i = _services.Count - 1; i >= 0; i--)
      {
        try
        { await _services[i].RemoveAsync(force: true, cancellationToken); }
        catch { /* best effort */ }
      }

      _services.Clear();
    }

    /// <inheritdoc />
    protected override async Task<ResourceDiagnostics> CollectDiagnosticsAsync(
        Exception failure,
        CancellationToken cancellationToken = default)
    {
      var diag = await base.CollectDiagnosticsAsync(failure, cancellationToken);

      if (Options.CaptureLogsOnFailure)
      {
        var logs = new List<string>();
        foreach (var container in _services.OfType<IContainerService>())
        {
          try
          {
            var log = await container.GetLogsAsync(false, cancellationToken);
            logs.Add($"--- {container.Name ?? container.Id} ---\n{log}");
          }
          catch (Exception ex)
          {
            Logger.Log($"Topology diagnostics log collection failed: {ex.Message}");
            logs.Add($"--- {container.Name ?? container.Id} --- (failed to collect)");
          }
        }

        diag.Logs = TruncateLogLines(string.Join("\n\n", logs));
      }

      return diag;
    }

    #endregion

    private void EnsureInitialized()
    {
      if (!IsInitialized)
        throw new InvalidOperationException(
            "Topology resource is not initialized. Call InitializeAsync first.");
    }
  }
}
