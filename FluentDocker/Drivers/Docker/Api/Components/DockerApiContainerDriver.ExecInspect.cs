using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.ApiModels;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  public partial class DockerApiContainerDriver
  {
    private async Task<ApiResult<ExecInspectResponse>> InspectExecExitCodeAsync(
        string execId, bool detach, DriverContext context, CancellationToken cancellationToken)
    {
      ApiResult<ExecInspectResponse> inspectResult = null!;
      var escapedExecId = Uri.EscapeDataString(execId);
      var deadline = DateTimeOffset.UtcNow +
          (context?.RequestTimeout ?? Context?.RequestTimeout ?? TimeSpan.FromMinutes(5));
      var delay = TimeSpan.FromMilliseconds(100);
      while (true)
      {
        inspectResult = await GetJsonAsync(
            $"/exec/{escapedExecId}/json",
            DockerApiJsonContext.Default.ExecInspectResponse, cancellationToken)
            .ConfigureAwait(false);
        if (!inspectResult.Success || detach ||
            (inspectResult.Data?.Running != true && inspectResult.Data?.ExitCode != null))
          return inspectResult;
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
          return inspectResult;
        await Task.Delay(remaining < delay ? remaining : delay, cancellationToken)
            .ConfigureAwait(false);
        delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
      }
    }
  }
}
