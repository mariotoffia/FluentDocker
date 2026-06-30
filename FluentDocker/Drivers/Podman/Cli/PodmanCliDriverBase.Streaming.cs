using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli
{
  /// <summary>
  /// Streaming half of <see cref="PodmanCliDriverBase"/>. Split into its own partial file
  /// purely to keep each source file within the repository's 500-line limit.
  /// </summary>
  public abstract partial class PodmanCliDriverBase
  {
    /// <summary>
    /// Executes a streaming Podman command asynchronously, yielding stdout line by line.
    /// stderr is drained concurrently (and bounded) so a chatty child cannot deadlock by
    /// filling the stderr pipe, and a non-zero exit is surfaced as a
    /// <see cref="DriverException"/> instead of ending the stream silently.
    /// </summary>
    protected async IAsyncEnumerable<string> ExecuteStreamingCommandAsync(
        string arguments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";

      var (processFileName, processArguments, passwordForStdin) =
          BuildSudoCommand(binaryPath, fullArgs, sudo, sudoPassword);

      using var process = new Process
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

      process.Start();

      if (passwordForStdin != null)
      {
        await process.StandardInput.WriteLineAsync(passwordForStdin).ConfigureAwait(false);
        process.StandardInput.Close();
      }

      // Drain stderr concurrently so a chatty child cannot deadlock by filling the stderr
      // pipe buffer while we only read stdout. The drain is bounded (truncating) so a
      // pathological child cannot force unbounded buffering.
      var errorTask = ReadBoundedTruncatingAsync(process.StandardError, MaxNonStreamingOutputBytes, cancellationToken);
      var reader = process.StandardOutput;
      string failure = null;

      try
      {
        string line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
          yield return line;

        // Stdout reached EOF — wait for the process and surface a non-zero exit as a failure
        // rather than ending the stream silently (a failed logs/events must throw).
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
          var trimmed = (error ?? string.Empty).Trim();
          if (trimmed.Length > 2000)
            trimmed = trimmed[..2000] + "…";
          failure = $"exit code {process.ExitCode}{(trimmed.Length == 0 ? string.Empty : $": {trimmed}")}";
        }
      }
      finally
      {
        KillProcessSafely(process, Logger);
        await ObserveQuietlyAsync(errorTask).ConfigureAwait(false);
      }

      if (failure != null)
        throw new DriverException($"Streaming command failed ({failure}).", ErrorCodes.Driver.CommandExecutionFailed);
    }

    /// <summary>
    /// Awaits a task, swallowing any fault — used to observe a best-effort background read
    /// (the stderr drain) when the stream is torn down early.
    /// </summary>
    private static async Task ObserveQuietlyAsync(Task task)
    {
      try
      {
        await task.ConfigureAwait(false);
      }
      catch (Exception)
      {
        // The stream is ending (early break/cancel); the drain result is irrelevant.
      }
    }
  }
}
