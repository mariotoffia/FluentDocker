using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;

namespace FluentDocker.Drivers.Podman.Cli
{
  /// <summary>
  /// Unbounded (inherently-long op) execution half of <see cref="PodmanCliDriverBase"/>. Split
  /// into its own partial file purely to keep each source file within the repository's 500-line
  /// limit.
  /// </summary>
  public abstract partial class PodmanCliDriverBase
  {
    /// <summary>
    /// Rolling-tail budget (chars) retained from an unbounded op's stdout/stderr. Unlike the
    /// bounded path — which FAILS a command whose stdout exceeds
    /// <see cref="MaxNonStreamingOutputBytes"/> — a long op streams line-by-line and keeps only
    /// the MOST RECENT output, so total output is not capped while memory stays bounded. Podman's
    /// meaningful trailing line (a <c>Loaded image:</c> line, an import/build image id, a
    /// container id, a wait exit code) always survives at the tail.
    /// </summary>
    // ponytail: a 256 KiB rolling tail keeps the trailing result line plus ample error context
    // for diagnostics without buffering a verbose pull/build/exec in full. Upgrade path: spool
    // the complete stream to a temp file if a caller ever needs the full output of a long op.
    private const int UnboundedTailBytes = 256 * 1024;

    /// <summary>
    /// Executes an inherently-long Podman op (pull/push/build/save/load/import/exec/wait/machine …)
    /// honoring ONLY caller cancellation, streaming stdout/stderr line-by-line into bounded rolling
    /// tails so a verbose-but-successful op is NOT failed at the 4 MiB buffered cap. Mirrors the
    /// sudo/global-arg handling of the bounded <c>ExecuteProcessAsync</c>.
    /// </summary>
    private async Task<SimpleCommandResult> ExecuteUnboundedProcessAsync(
        string arguments, CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
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

        process.Start();

        if (passwordForStdin != null)
        {
          await process.StandardInput.WriteLineAsync(passwordForStdin.AsMemory(), cancellationToken).ConfigureAwait(false);
          process.StandardInput.Close();
        }

        // Read both pipes concurrently so neither deadlocks on a full buffer; each is kept as a
        // bounded rolling tail. The tail preserves the END of the stream — where podman prints the
        // meaningful result line and the freshest error context.
        var outTail = new OutputTail(UnboundedTailBytes);
        var errTail = new OutputTail(UnboundedTailBytes);
        var outTask = ReadTailAsync(process.StandardOutput, outTail, cancellationToken);
        var errTask = ReadTailAsync(process.StandardError, errTail, cancellationToken);
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
        // Unbounded ops disable the timeout, so an OCE is always caller-driven: kill the child to
        // avoid an orphan and rethrow the caller's intent bound to their token.
        KillProcessSafely(process, Logger);
        throw;
      }
      catch (Exception ex)
      {
        // Non-cancellation failure (e.g. a pipe I/O error): kill the still-writing child so it is
        // not orphaned, and surface a clean -1 result carrying the message.
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

    /// <summary>
    /// Streams a text reader line-by-line into a bounded rolling <paramref name="tail"/> until EOF,
    /// never buffering the whole stream.
    /// </summary>
    private static async Task ReadTailAsync(TextReader reader, OutputTail tail, CancellationToken cancellationToken)
    {
      string line;
      while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        tail.Append(line);
    }

    /// <summary>
    /// A bounded, newline-joined rolling tail: appends lines and drops the oldest once the total
    /// retained character budget is exceeded, so an unbounded stream never grows memory without
    /// limit yet the most-recent output is preserved verbatim.
    /// </summary>
    private sealed class OutputTail
    {
      private readonly Queue<string> _lines = new();
      private readonly int _maxChars;
      private int _chars;

      public OutputTail(int maxChars) => _maxChars = maxChars;

      public void Append(string line)
      {
        _lines.Enqueue(line);
        _chars += line.Length + 1; // +1 for the '\n' re-inserted on join
        while (_chars > _maxChars && _lines.Count > 1)
          _chars -= _lines.Dequeue().Length + 1;
      }

      public override string ToString() => string.Join("\n", _lines);
    }
  }
}
