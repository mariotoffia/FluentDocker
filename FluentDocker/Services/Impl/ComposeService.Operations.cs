#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Mutating compose operations (exec / scale / restart). Split from the main
  /// <see cref="ComposeService"/> file purely to keep each source file within the
  /// repository's 500-line limit.
  /// </summary>
  public partial class ComposeService
  {
    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string service, string[] command, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeExecConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        Service = service,
        Command = command
      };

      var response = await driver.ExecuteAsync(context, config, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to execute command in service '{service}' for project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    /// <inheritdoc />
    public async Task ScaleAsync(string service, int replicas, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeScaleConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        Scale = new Dictionary<string, int> { { service, replicas } }
      };

      var response = await driver.ScaleAsync(context, config, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to scale service '{service}' for project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }
    }

    /// <inheritdoc />
    public async Task RestartAsync(IEnumerable<string> services, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (_state == ServiceRunningState.Removed)
        throw new InvalidOperationException("Cannot restart a removed compose project.");

      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeRestartConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        Services = services is null ? [] : [.. services]
      };

      try
      {
        var response = await driver.RestartAsync(context, config, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
          throw new DriverException(
              $"Failed to restart compose project '{_projectName}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        // `docker compose restart` returns success even when a service crashes on boot, so a
        // whole-project restart must reconcile the same way the per-service branch does instead
        // of optimistically reporting Running for a dead project (SVC-MAJ-3). Reconcile is
        // best-effort: it corrects the optimistic state but leaves it if the ps probe fails.
        UpdateState(ServiceRunningState.Running);
        await TryReconcileStateAsync(cancellationToken).ConfigureAwait(false);
        if (_state == ServiceRunningState.Running)
          await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
      }
      catch
      {
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
        throw;
      }
    }
  }
}
