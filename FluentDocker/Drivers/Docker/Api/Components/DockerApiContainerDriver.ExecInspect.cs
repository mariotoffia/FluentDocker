using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.ApiModels;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  public partial class DockerApiContainerDriver
  {
    private async Task<ApiResult<ExecInspectResponse>> InspectExecExitCodeAsync(
        string execId, bool detach, CancellationToken cancellationToken)
    {
      ApiResult<ExecInspectResponse> inspectResult = null!;
      var escapedExecId = Uri.EscapeDataString(execId);
      for (var attempt = 0; attempt < 5; attempt++)
      {
        inspectResult = await GetJsonAsync(
            $"/exec/{escapedExecId}/json",
            DockerApiJsonContext.Default.ExecInspectResponse, cancellationToken)
            .ConfigureAwait(false);
        if (!inspectResult.Success || detach ||
            (inspectResult.Data?.Running != true && inspectResult.Data?.ExitCode != null))
          return inspectResult;
        if (attempt < 4)
          await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
      }
      return inspectResult;
    }
  }
}
