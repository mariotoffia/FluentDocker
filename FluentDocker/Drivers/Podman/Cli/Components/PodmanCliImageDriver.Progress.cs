using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  public partial class PodmanCliImageDriver
  {
    private async Task<SimpleCommandResult> ExecuteProgressCommandAsync<TProgress>(
        DriverContext context,
        string arguments,
        IProgress<TProgress>? progress,
        Func<string, TProgress> createProgress,
        CancellationToken cancellationToken)
    {
      try
      {
        var output = new ProgressOutputTail();
        await foreach (var line in ExecuteStreamingCommandWithProgressAsync(context, arguments, cancellationToken).ConfigureAwait(false))
        {
          output.Append(line);
          progress?.Report(createProgress(line));
        }

        return new SimpleCommandResult { Success = true, Output = output.ToString(), ExitCode = 0 };
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

    private sealed class ProgressOutputTail
    {
      private readonly Queue<string> _lines = new();
      private int _chars;
      private bool _truncated;

      public void Append(string line)
      {
        _lines.Enqueue(line);
        _chars += line.Length + 1;
        while (_chars > CliOutputTruncation.DefaultTailChars && _lines.Count > 1)
        {
          _truncated = true;
          _chars -= _lines.Dequeue().Length + 1;
        }
      }

      public override string ToString()
      {
        var tail = string.Join("\n", _lines);
        return _truncated
            ? $"{CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars)}\n{tail}"
            : tail;
      }
    }
  }
}
