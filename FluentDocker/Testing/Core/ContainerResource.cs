using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A single-container test resource with async lifecycle.
  /// </summary>
  public class ContainerResource : ResourceBase
  {
    private readonly Action<IContainerBuilder> _configure;
    private static readonly Action<ILogger, Exception> DiagnosticsLogCollectionFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(DiagnosticsLogCollectionFailed)),
            "Container diagnostics log collection failed.");
    private static readonly Action<ILogger, Exception> DiagnosticsInspectCollectionFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(DiagnosticsInspectCollectionFailed)),
            "Container diagnostics inspect collection failed.");
    private static readonly Action<ILogger, Exception> LateContainerProvisionCleanupFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, nameof(LateContainerProvisionCleanupFailed)),
            "Late stale container provision cleanup failed.");

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
        : base(kernel, options)
    {
      ArgumentNullException.ThrowIfNull(configure);
      _configure = configure;
    }

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
      await CapabilityChecks.EnsureContainerSupportAsync(Kernel, DriverId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var generation = ProvisionGeneration;
      var builder = new Builder();
      builder.WithinDriver(DriverId, Kernel);
      builder.UseContainer(c =>
      {
        _configure(c);
        if (Options.EnableSessionLabels)
        {
          foreach (var label in SessionLabel.CreateLabels(Options.SessionId))
            c.WithLabel(label.Key, label.Value);
        }
      });

      var results = await builder.BuildAsync(
          cleanupTimeout: Options.TeardownTimeout,
          cancellationToken: cancellationToken).ConfigureAwait(false);
      if (results.All.Count > 0 && results.All[0] is IContainerService container)
      {
        if (TryCommitProvision(generation, () =>
        {
          Container = container;
          ResourceName = container.Name ?? container.Id;
        }))
        {
          return;
        }

        await RemoveStaleContainerAsync(container).ConfigureAwait(false);
      }
      else
      {
        throw new InvalidOperationException("Builder did not produce a container service");
      }
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      if (Container == null)
        return;

      await Container.StopAsync(cancellationToken).ConfigureAwait(false);
      await Container.RemoveAsync(force: false, cancellationToken).ConfigureAwait(false);
      Container = null;
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      var c = Container;
      if (c == null)
        return;

      try
      {
        await c.RemoveAsync(force: true, cancellationToken).ConfigureAwait(false);
        Container = null;
      }
      catch (DriverException ex) when (ex.ErrorCode == ErrorCodes.Container.NotFound)
      {
        Container = null;
      }
    }

    /// <inheritdoc />
    protected override async Task<ResourceDiagnostics> CollectDiagnosticsAsync(
        Exception failure,
        CancellationToken cancellationToken = default)
    {
      var diag = await base.CollectDiagnosticsAsync(failure, cancellationToken).ConfigureAwait(false);

      if (Options.CaptureLogsOnFailure)
      {
        var builderTail = ExtractBuilderLogTail(failure);
        if (!string.IsNullOrWhiteSpace(builderTail))
          diag.Logs = TruncateLogLines(builderTail);
      }

      if (Container != null && Options.CaptureLogsOnFailure)
      {
        try
        {
          if (string.IsNullOrEmpty(diag.Logs))
          {
            diag.Logs = TruncateLogLines(
                await Container.GetLogsAsync(false, cancellationToken).ConfigureAwait(false));
          }
        }
        catch (Exception ex)
        {
          DiagnosticsLogCollectionFailed(Logger, ex);
          diag.Logs = "(failed to collect logs)";
        }

        try
        {
          var info = await Container.InspectAsync(cancellationToken).ConfigureAwait(false);
          diag.InspectPayload = info != null
              ? JsonHelper.SerializeIndented(info)
              : null;
        }
        catch (Exception ex)
        {
          DiagnosticsInspectCollectionFailed(Logger, ex);
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

    private async Task RemoveStaleContainerAsync(IContainerService container)
    {
      try
      {
        using var cts = new CancellationTokenSource(Options.TeardownTimeout);
        var removeTask = container.RemoveAsync(force: true, cancellationToken: cts.Token);
        await removeTask.WaitAsync(cts.Token).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        OrphanCleanup.MarkAbandonedLateProvision(container.Name ?? container.Id, Options.SessionId);
        LateContainerProvisionCleanupFailed(Logger, ex);
      }
    }

    private static string ExtractBuilderLogTail(Exception failure)
    {
      const string dataKey = "ContainerLogTail";
      const string marker = "Container log tail:";
      for (var ex = failure; ex != null; ex = ex.InnerException)
      {
        if (ex.Data.Contains(dataKey) &&
            ex.Data[dataKey] is string dataTail &&
            !string.IsNullOrWhiteSpace(dataTail))
          return dataTail;

        var markerIndex = ex.Message.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex >= 0)
          return ex.Message[(markerIndex + marker.Length)..].Trim();
      }

      return null;
    }
  }
}
