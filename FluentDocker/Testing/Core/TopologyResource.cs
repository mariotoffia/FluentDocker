using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A multi-container topology with optional shared networks and volumes.
  /// Containers are started in declaration order and torn down in reverse order.
  /// </summary>
  public class TopologyResource : ResourceBase
  {
    private readonly Action<Builder> _configure;
    private IReadOnlyList<IServiceAsync> _services = [];
    private static readonly Action<ILogger, Exception> DiagnosticsLogCollectionFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(DiagnosticsLogCollectionFailed)),
            "Topology diagnostics log collection failed.");

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
      get { EnsureInitialized(); return _services; }
    }

    /// <summary>
    /// Gets a container service by name.
    /// </summary>
    public IContainerService GetContainer(string name)
    {
      EnsureInitialized();
      var requested = NormalizeContainerName(name);
      return _services.OfType<IContainerService>()
          .FirstOrDefault(c => NormalizeContainerName(c.Name) == requested);
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
      get { EnsureInitialized(); return [.. _services.OfType<IContainerService>()]; }
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      // Topology requires at least container support.
      await CapabilityChecks.EnsureContainerSupportAsync(Kernel, DriverId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var generation = ProvisionGeneration;
      var builder = new Builder();
      builder.WithinDriver(DriverId, Kernel);
      _configure(builder);
      ApplySessionLabels(builder);

      var results = await builder.BuildAsync(
          cleanupTimeout: Options.TeardownTimeout,
          cancellationToken: cancellationToken).ConfigureAwait(false);
      var services = results.All.OfType<IServiceAsync>().ToArray();
      if (TryCommitProvision(generation, () =>
      {
        _services = services;
        ResourceName = $"topology-{services.Length}-services";
      }))
      {
        return;
      }

      await RemoveStaleServicesAsync(services).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      var failures = new List<Exception>();

      // Tear down in reverse order for dependency safety.
      for (var i = _services.Count - 1; i >= 0; i--)
      {
        try
        { await _services[i].StopAsync(cancellationToken).ConfigureAwait(false); }
        catch { /* stop failure must not prevent removal */ }

        try
        { await _services[i].RemoveAsync(force: false, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { failures.Add(ex); }
      }

      if (failures.Count > 0)
        throw new AggregateException(
            $"{failures.Count} service(s) failed to remove during teardown.",
            failures);

      _services = [];
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      var failures = new List<Exception>();
      for (var i = _services.Count - 1; i >= 0; i--)
      {
        try
        { await _services[i].RemoveAsync(force: true, cancellationToken).ConfigureAwait(false); }
        catch (DriverException ex) when (IsNotFound(ex)) { /* already gone */ }
        catch (Exception ex) { failures.Add(ex); }
      }

      if (failures.Count > 0)
        throw new AggregateException(
            $"{failures.Count} service(s) failed to force-remove.",
            failures);

      _services = [];
    }

    /// <inheritdoc />
    protected override async Task<ResourceDiagnostics> CollectDiagnosticsAsync(
        Exception failure,
        CancellationToken cancellationToken = default)
    {
      var diag = await base.CollectDiagnosticsAsync(failure, cancellationToken).ConfigureAwait(false);

      if (Options.CaptureLogsOnFailure)
      {
        var logs = new List<string>();
        foreach (var container in _services.OfType<IContainerService>())
        {
          try
          {
            var log = await container.GetLogsAsync(false, cancellationToken).ConfigureAwait(false);
            logs.Add($"--- {container.Name ?? container.Id} ---\n{log}");
          }
          catch (Exception ex)
          {
            DiagnosticsLogCollectionFailed(Logger, ex);
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

    private static bool IsNotFound(DriverException ex)
    {
      return ex.ErrorCode == ErrorCodes.Container.NotFound ||
             ex.ErrorCode == ErrorCodes.Network.NotFound ||
             ex.ErrorCode == ErrorCodes.Volume.NotFound ||
             ex.ErrorCode == ErrorCodes.Driver.NotFound ||
             ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeContainerName(string name) =>
        name?.Trim().TrimStart('/');

    private void ApplySessionLabels(Builder builder)
    {
      if (!Options.EnableSessionLabels)
        return;

      var labels = SessionLabel.CreateLabels(Options.SessionId);
      foreach (var child in builder.ResourceBuilders)
      {
        foreach (var label in labels)
          ApplyLabel(child, label.Key, label.Value);
      }
    }

    private static void ApplyLabel(object builder, string key, string value)
    {
      switch (builder)
      {
        case IContainerBuilder container:
          container.WithLabel(key, value);
          break;
        case INetworkBuilder network:
          network.WithLabel(key, value);
          break;
        case IVolumeBuilder volume:
          volume.WithLabel(key, value);
          break;
      }
    }

    private async Task RemoveStaleServicesAsync(IReadOnlyList<IServiceAsync> services)
    {
      for (var i = services.Count - 1; i >= 0; i--)
      {
        try
        {
          using var cts = new CancellationTokenSource(Options.TeardownTimeout);
          await services[i].RemoveAsync(force: true, cts.Token).ConfigureAwait(false);
        }
        catch
        {
          OrphanCleanup.MarkAbandonedLateProvision(services[i].Name, Options.SessionId);
        }
      }
    }
  }
}
