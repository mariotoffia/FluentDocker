using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
  public partial class ComposeService
  {
    /// <inheritdoc />
    public async Task UnpauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (_state == ServiceRunningState.Removed)
        throw new InvalidOperationException("Cannot unpause a removed compose project.");

      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var config = new ComposeFileConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName
      };

      try
      {
        var response = await driver.UnpauseAsync(context, config, cancellationToken).ConfigureAwait(false);
        if (!response.Success)
        {
          throw new DriverException(
              $"Failed to unpause compose project '{_projectName}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        // Running hooks must not fire until reconcile confirms the project is genuinely
        // running — same optimistic-state ordering as StartAsync/RestartAsync (S-H2):
        // `docker compose unpause` succeeding does not prove every service is up.
        UpdateState(ServiceRunningState.Running);
        await TryReconcileStateAsync(cancellationToken).ConfigureAwait(false);
        if (_state == ServiceRunningState.Running)
          await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }
  }
}
