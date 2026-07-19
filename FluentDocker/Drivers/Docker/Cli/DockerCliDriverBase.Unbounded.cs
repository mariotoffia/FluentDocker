#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli
{
  public abstract partial class DockerCliDriverBase
  {
    private async Task<SimpleCommandResult> ExecuteUnboundedProcessAsync(
        DriverContext context,
        string arguments,
        IDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      // Environment names are forwarded through sudo via --preserve-env — sudo's env_reset
      // would otherwise silently strip variables set on the spawned sudo process (DC-2).
      var (processFileName, processArguments, passwordForStdin) =
          BuildSudoCommand(binaryPath, fullArgs, sudo, sudoPassword,
              ValidatedPreserveEnvNames(environment, sudo));

      Process process = null;
      Task outTask = null;
      Task errTask = null;
      var outTail = new OutputTail(CliOutputTruncation.DefaultTailChars);
      var errTail = new OutputTail(CliOutputTruncation.DefaultTailChars);
      var processStarted = false;
      try
      {
        process = new Process
        {
          StartInfo = new ProcessStartInfo
          {
            FileName = processFileName,
            Arguments = processArguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = Utf8NoBom
          }
        };

        if (environment != null)
          foreach (var kvp in environment)
            process.StartInfo.Environment[kvp.Key] = kvp.Value;

        StartProcessOrThrow(process, binaryPath);
        processStarted = true;

        outTask = ReadTailAsync(process.StandardOutput, outTail, cancellationToken);
        errTask = ReadTailAsync(process.StandardError, errTail, cancellationToken);

        var stdinFailure = await TryWriteStandardInputAsync(process, passwordForStdin, null, cancellationToken).ConfigureAwait(false);

        await Task.WhenAll(outTask, errTask).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new SimpleCommandResult
        {
          Success = process.ExitCode == 0,
          Output = outTail.ToString(),
          Error = string.IsNullOrEmpty(errTail.ToString()) && stdinFailure != null ? stdinFailure.Message : errTail.ToString(),
          ExitCode = process.ExitCode
        };
      }
      catch (OperationCanceledException)
      {
        KillProcessSafely(process, Logger);
        await TryObserveTaskAsync(outTask).ConfigureAwait(false);
        await TryObserveTaskAsync(errTask).ConfigureAwait(false);
        throw;
      }
      catch (Exception ex) when (processStarted || ex is not DriverException)
      {
        KillProcessSafely(process, Logger);
        await TryObserveTaskAsync(outTask).ConfigureAwait(false);
        await TryObserveTaskAsync(errTask).ConfigureAwait(false);
        return new SimpleCommandResult
        {
          Success = false,
          Output = outTail.ToString(),
          Error = string.IsNullOrEmpty(errTail.ToString()) ? ex.Message : errTail.ToString(),
          ExitCode = GetExitCodeOrDefault(process)
        };
      }
      finally
      {
        process?.Dispose();
      }
    }

    private static async Task TryObserveTaskAsync(Task task)
    {
      if (task == null)
        return;
      try
      {
        await task.ConfigureAwait(false);
      }
      catch
      {
        // best effort drain/observe
      }
    }

    private static async Task ReadTailAsync(TextReader reader, OutputTail tail, CancellationToken cancellationToken)
    {
      var buffer = new char[8192];
      int read;
      while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        tail.Append(buffer, read);
    }

    private sealed class OutputTail
    {
      private readonly StringBuilder _tail = new();
      private readonly int _maxChars;
      private bool _truncated;

      public OutputTail(int maxChars) => _maxChars = maxChars;

      public void Append(char[] buffer, int count)
      {
        _tail.Append(buffer, 0, count);
        if (_tail.Length <= _maxChars)
          return;

        _truncated = true;
        _tail.Remove(0, _tail.Length - _maxChars);
      }

      public override string ToString()
      {
        return _truncated
            ? $"{CliOutputTruncation.Marker(_maxChars)}\n{_tail}"
            : _tail.ToString();
      }
    }
  }
}
