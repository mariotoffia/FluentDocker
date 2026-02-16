using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Services;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A Docker Compose test resource with async lifecycle.
  /// </summary>
  public class ComposeResource : ResourceBase
  {
    private readonly Action<IComposeBuilder> _configure;

    /// <summary>
    /// Creates a compose resource.
    /// </summary>
    /// <param name="kernel">Kernel with registered drivers.</param>
    /// <param name="configure">Compose builder configuration callback.</param>
    /// <param name="options">Optional resource options.</param>
    public ComposeResource(
        FluentDockerKernel kernel,
        Action<IComposeBuilder> configure,
        DockerResourceOptions options = null)
        : base(kernel, options) => _configure = configure ?? throw new ArgumentNullException(nameof(configure));

    /// <summary>
    /// The running compose service, available after initialization.
    /// </summary>
    public IComposeService Service { get; private set; }

    /// <summary>
    /// Lists all services in the compose project.
    /// </summary>
    public Task<IList<ComposeServiceInfo>> ListServicesAsync(
        CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      return Service.ListServicesAsync(cancellationToken);
    }

    /// <summary>
    /// Gets compose logs.
    /// </summary>
    public Task<string> GetLogsAsync(CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      return Service.GetLogsAsync(false, cancellationToken);
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      await CapabilityChecks.EnsureComposeSupportAsync(Kernel, DriverId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var builder = new Builder();
      builder.WithinDriver(DriverId, Kernel);
      builder.UseCompose(_configure);

      var results = await builder.BuildAsync(cancellationToken);
      if (results.All.Count > 0 && results.All[0] is IComposeService compose)
      {
        Service = compose;
        ResourceName = compose.ProjectName ?? compose.Name;
      }
      else
      {
        throw new InvalidOperationException("Builder did not produce a compose service");
      }
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      if (Service == null)
        return;

      await Service.StopAsync(cancellationToken);
      await Service.RemoveAsync(force: false, cancellationToken);
      Service = null;
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      var s = Service;
      Service = null;
      if (s == null)
        return;

      try
      { await s.RemoveAsync(force: true, cancellationToken); }
      catch { /* best effort */ }
    }

    /// <inheritdoc />
    protected override async Task<ResourceDiagnostics> CollectDiagnosticsAsync(Exception failure)
    {
      var diag = await base.CollectDiagnosticsAsync(failure);

      if (Service != null && Options.CaptureLogsOnFailure)
      {
        try
        {
          diag.Logs = TruncateLogLines(await Service.GetLogsAsync(false));
        }
        catch
        {
          diag.Logs = "(failed to collect compose logs)";
        }
      }

      return diag;
    }

    #endregion

    private void EnsureInitialized()
    {
      if (!IsInitialized || Service == null)
        throw new InvalidOperationException(
            "Compose resource is not initialized. Call InitializeAsync first.");
    }
  }
}
