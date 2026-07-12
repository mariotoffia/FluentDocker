using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli
{
  public abstract partial class DockerCliDriverBase
  {
    private static void AddTail(Queue<string> tail, string line)
    {
      if (tail.Count == 10)
        tail.Dequeue();
      tail.Enqueue(line);
    }

    private static string FormatTail(Queue<string> tail)
    {
      if (tail.Count == 0)
        return string.Empty;

      var text = string.Join(Environment.NewLine, tail).Trim();
      if (text.Length > 2000)
        text = text[^2000..];
      return $": {text}";
    }

    private static async Task PumpBothStreamsAsync(
        Process process, System.Threading.Channels.ChannelWriter<string> writer, CancellationToken cancellationToken)
    {
      async Task PumpAsync(TextReader reader)
      {
        var lineReader = new BoundedLineReader(reader);
        string line;
        while ((line = await lineReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
          await writer.WriteAsync(line, cancellationToken).ConfigureAwait(false);
      }

      try
      {
        await Task.WhenAll(PumpAsync(process.StandardOutput), PumpAsync(process.StandardError)).ConfigureAwait(false);
        writer.TryComplete();
      }
      catch (Exception ex)
      {
        writer.TryComplete(ex);
      }
    }

    private static async Task ObserveQuietlyAsync(Task task)
    {
      try
      {
        await task.ConfigureAwait(false);
      }
      catch (Exception)
      {
        // The stream is ending; the drain result is irrelevant.
      }
    }

    /// <summary>
    /// Executes a streaming Docker command that emits its progress on <b>stderr</b>
    /// (e.g. <c>docker model pull</c>), interleaving stdout and stderr lines into a
    /// single ordered-by-arrival sequence so progress text is actually observed.
    /// <para>
    /// This is intentionally a separate path from
    /// <see cref="ExecuteStreamingCommandAsync(string, CancellationToken)"/>, which yields
    /// only stdout and is used by logs/events/stats consumers that must NOT have stderr
    /// folded into their output. A non-zero exit is surfaced as a
    /// <see cref="DriverException"/> exactly as the stdout-only variant does.
    /// </para>
    /// </summary>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stdout and stderr lines, in arrival order.</returns>
    protected async IAsyncEnumerable<string> ExecuteStreamingCommandWithProgressAsync(
        string arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      await foreach (var line in ExecuteStreamingCommandWithProgressAsync((DriverContext)null, arguments, cancellationToken).ConfigureAwait(false))
        yield return line;
    }

    /// <summary>
    /// Executes a streaming Docker command using the given driver context, interleaving
    /// stdout and stderr lines into a single arrival-ordered sequence so progress text on
    /// stderr (e.g. <c>docker model pull</c>) is actually observed.
    /// </summary>
    /// <param name="context">Driver context supplying host/TLS/sudo settings.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stdout and stderr lines, in arrival order.</returns>
    /// <exception cref="DriverException">The process exited with a non-zero code.</exception>
    protected async IAsyncEnumerable<string> ExecuteStreamingCommandWithProgressAsync(
        DriverContext context, string arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext);
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

      if (process.StartInfo.RedirectStandardInput)
        process.StartInfo.StandardInputEncoding = Utf8NoBom;

      StartProcessOrThrow(process, binaryPath);

      // Both pipes are read line-by-line and merged into a bounded channel so neither
      // can deadlock by filling its pipe buffer while only the other is consumed.
      var channel = System.Threading.Channels.Channel.CreateBounded<string>(
          new System.Threading.Channels.BoundedChannelOptions(256)
          {
            SingleReader = true,
            SingleWriter = false,
            FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
          });

      var pump = PumpBothStreamsAsync(process, channel.Writer, cancellationToken);
      string failure = null;
      var failureExitCode = 0;
      var tail = new Queue<string>();

      try
      {
        // Write the sudo password inside the guarded region so a broken-pipe write still hits
        // KillProcessSafely in finally instead of orphaning the child (DCLI-MAJ-1).
        if (passwordForStdin != null)
          _ = await TryWriteStandardInputAsync(process, passwordForStdin, null, cancellationToken)
              .ConfigureAwait(false);

        await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
          AddTail(tail, line);
          yield return line;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
          failureExitCode = process.ExitCode;
          failure = $"exit code {FormatInvariant(process.ExitCode)}{FormatTail(tail)}";
        }
      }
      finally
      {
        // Complete the writer FIRST. If the consumer abandoned enumeration early (break,
        // Take(n), or a throwing body) without cancelling, both pump tasks may be parked in
        // WriteAsync on the full bounded channel — and killing the process does NOT free
        // them (they are blocked on the channel, not on ReadLine). Completing the writer
        // makes those parked writes throw ChannelClosedException (absorbed by the pump), so
        // ObserveQuietlyAsync below can never hang.
        channel.Writer.TryComplete();
        KillProcessSafely(process, Logger);
        await ObserveQuietlyAsync(pump).ConfigureAwait(false);
      }

      if (failure != null)
      {
        throw new DriverException(
            $"Streaming command failed ({failure}).",
            ErrorCodes.Driver.CommandExecutionFailed,
            new ErrorContext("StreamingCommand") { ExitCode = failureExitCode, StdErr = $"merged output{FormatTail(tail)}" });
      }
    }
  }
}
