using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
  public partial class ComposeService
  {
    /// <summary>
    /// Tears down the compose project. Volumes are removed only when configured with
    /// <c>WithRemoveVolumes()</c>; <paramref name="force"/> is retained for API compatibility
    /// and has no effect for compose teardown.
    /// </summary>
    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      // Idempotent client-side: a second remove (typically dispose after an explicit
      // RemoveAsync) must not re-run `compose down` or re-fire Removed hooks.
      if (_state == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeDownConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        RemoveVolumes = _removeVolumes,
        RemoveImages = _removeImages ? "all" : null
      };

      try
      {
        UpdateState(ServiceRunningState.Removing);
        await ExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);

        var response = await driver.DownAsync(context, config, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
          throw new DriverException(
              $"Failed to remove compose project '{_projectName}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        UpdateState(ServiceRunningState.Removed);
        await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }
  }
}
