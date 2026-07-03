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
      var (processFileName, processArguments, passwordForStdin) =
          BuildSudoCommand(binaryPath, fullArgs, sudo, sudoPassword);

      Process process = null;
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
            RedirectStandardInput = passwordForStdin != null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
          }
        };

        if (environment != null)
          foreach (var kvp in environment)
            process.StartInfo.Environment[kvp.Key] = kvp.Value;

        process.Start();

        var outTail = new OutputTail(CliOutputTruncation.DefaultTailBytes);
        var errTail = new OutputTail(CliOutputTruncation.DefaultTailBytes);
        var outTask = ReadTailAsync(process.StandardOutput, outTail, cancellationToken);
        var errTask = ReadTailAsync(process.StandardError, errTail, cancellationToken);

        if (passwordForStdin != null)
        {
          await process.StandardInput.WriteLineAsync(passwordForStdin.AsMemory(), cancellationToken).ConfigureAwait(false);
          process.StandardInput.Close();
        }

        await Task.WhenAll(outTask, errTask).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new SimpleCommandResult
        {
          Success = process.ExitCode == 0,
          Output = outTail.ToString(),
          Error = errTail.ToString(),
          ExitCode = process.ExitCode
        };
      }
      catch (OperationCanceledException)
      {
        KillProcessSafely(process, Logger);
        throw;
      }
      catch (Exception ex)
      {
        KillProcessSafely(process, Logger);
        return new SimpleCommandResult
        {
          Success = false,
          Error = ex.Message,
          ExitCode = -1
        };
      }
      finally
      {
        process?.Dispose();
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
