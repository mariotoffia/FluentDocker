using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
  public partial class ComposeService
  {
    public async Task UnpauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var config = new ComposeFileConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName
      };

      var response = await driver.UnpauseAsync(context, config, cancellationToken).ConfigureAwait(false);
      if (!response.Success)
      {
        UpdateState(ServiceRunningState.Unknown);
        throw new DriverException(
            $"Failed to unpause compose project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Running);
      await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
    }
  }
}
