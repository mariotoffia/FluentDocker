using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  public partial class DockerCliImageDriver
  {
    private static string CreateIidFilePath()
    {
      var iidDirectory = Path.GetTempPath();
      Directory.CreateDirectory(iidDirectory);
      return Path.Combine(iidDirectory, $"docker-iid-{Guid.NewGuid():N}");
    }

    private async Task<SimpleCommandResult> ExecuteProgressCommandAsync<TProgress>(
        DriverContext context,
        string arguments,
        IProgress<TProgress> progress,
        Func<string, TProgress> createProgress,
        CancellationToken cancellationToken)
    {
      try
      {
        await foreach (var line in ExecuteStreamingCommandWithProgressAsync(context, arguments, cancellationToken).ConfigureAwait(false))
          progress?.Report(createProgress(line));

        return new SimpleCommandResult { Success = true, ExitCode = 0 };
      }
      catch (DriverException ex)
      {
        return new SimpleCommandResult
        {
          Success = false,
          Error = ex.Message,
          ExitCode = ex.Context?.ExitCode ?? -1
        };
      }
    }

    private static ImagePullProgress CreatePullProgress(string line) =>
        new() { Status = line };

    private static ImagePushProgress CreatePushProgress(string line) =>
        new() { Status = line };

    private static ImageBuildProgress CreateBuildProgress(string line) =>
        new() { Stream = line, Status = line };
  }
}
