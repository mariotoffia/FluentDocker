using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
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
        Container = container;
        ResourceName = container.Name ?? container.Id;
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
      Container = null;
      if (c == null)
        return;

      try
      { await c.RemoveAsync(force: true, cancellationToken).ConfigureAwait(false); }
      catch { /* best effort */ }
    }

    /// <inheritdoc />
    protected override async Task<ResourceDiagnostics> CollectDiagnosticsAsync(
        Exception failure,
        CancellationToken cancellationToken = default)
    {
      var diag = await base.CollectDiagnosticsAsync(failure, cancellationToken).ConfigureAwait(false);

      if (Container != null && Options.CaptureLogsOnFailure)
      {
        try
        {
          diag.Logs = TruncateLogLines(
              await Container.GetLogsAsync(false, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
          Logger.LogWarning(ex, "Container diagnostics log collection failed");
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
          Logger.LogWarning(ex, "Container diagnostics inspect collection failed");
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
