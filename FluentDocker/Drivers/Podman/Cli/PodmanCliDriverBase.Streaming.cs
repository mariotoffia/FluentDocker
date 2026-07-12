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
      await foreach (var line in ExecuteStreamingCommandAsync(
          null, arguments, cancellationToken).ConfigureAwait(false))
        yield return line;
    }

    /// <summary>
    /// Executes a streaming Podman command using the given driver context, yielding stdout
    /// lines as they arrive. stderr is drained concurrently (bounded, not yielded) so a chatty
    /// child cannot deadlock, and a non-zero exit is surfaced as a <see cref="DriverException"/>.
    /// </summary>
    /// <param name="context">Driver context supplying host/sudo settings.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of stdout lines.</returns>
    /// <exception cref="DriverException">The process exited with a non-zero code.</exception>
    protected async IAsyncEnumerable<string> ExecuteStreamingCommandAsync(
        DriverContext context,
        string arguments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext, Logger);
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

      if (passwordForStdin != null)
        _ = await TryWriteStandardInputAsync(process, passwordForStdin, null, cancellationToken).ConfigureAwait(false);

      // Drain stderr concurrently so a chatty child cannot deadlock by filling the stderr
      // pipe buffer while we only read stdout. The drain is bounded (truncating) so a
      // pathological child cannot force unbounded buffering.
      var errorTask = ReadBoundedTruncatingAsync(process.StandardError, MaxNonStreamingErrorBytes, cancellationToken);
      var reader = new BoundedLineReader(process.StandardOutput);
      string failure = null;
      var failureExitCode = 0;
      string failureError = null;

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
          failureExitCode = process.ExitCode;
          var trimmed = (error ?? string.Empty).Trim();
          if (trimmed.Length > 2000)
            trimmed = trimmed[..2000] + "…";
          failureError = trimmed;
          failure = $"exit code {process.ExitCode}{(trimmed.Length == 0 ? string.Empty : $": {trimmed}")}";
        }
      }
      finally
      {
        KillProcessSafely(process, Logger);
        await ObserveQuietlyAsync(errorTask).ConfigureAwait(false);
      }

      if (failure != null)
        throw CreateCommandFailureException(
            effectiveContext,
            $"Streaming command failed ({failure}).",
            ErrorCodes.Driver.CommandExecutionFailed,
            new ErrorContext("StreamingCommand")
            {
              DriverId = effectiveContext.DriverId,
              Host = effectiveContext.Host,
              ExitCode = failureExitCode,
              StdErr = failureError
            });
    }

    /// <summary>
    /// Executes a streaming Podman command, interleaving stdout and stderr lines into a single
    /// arrival-ordered sequence so progress text on stderr is actually observed.
    /// </summary>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stdout and stderr lines, in arrival order.</returns>
    protected async IAsyncEnumerable<string> ExecuteStreamingCommandWithProgressAsync(
        string arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      await foreach (var line in ExecuteStreamingCommandWithProgressAsync(
          null, arguments, cancellationToken).ConfigureAwait(false))
        yield return line;
    }

    /// <summary>
    /// Executes a streaming Podman command using the given driver context, interleaving stdout
    /// and stderr lines into a single arrival-ordered sequence so progress text on stderr
    /// (e.g. pull/push progress) is actually observed.
    /// </summary>
    /// <param name="context">Driver context supplying host/sudo settings.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stdout and stderr lines, in arrival order.</returns>
    /// <exception cref="DriverException">The process exited with a non-zero code.</exception>
    protected async IAsyncEnumerable<string> ExecuteStreamingCommandWithProgressAsync(
        DriverContext context,
        string arguments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext, Logger);
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

      if (passwordForStdin != null)
        _ = await TryWriteStandardInputAsync(process, passwordForStdin, null, cancellationToken).ConfigureAwait(false);

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
        await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
          AddTail(tail, line);
          yield return line;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
          failureExitCode = process.ExitCode;
          failure = $"exit code {process.ExitCode}{FormatTail(tail)}";
        }
      }
      finally
      {
        channel.Writer.TryComplete();
        KillProcessSafely(process, Logger);
        await ObserveQuietlyAsync(pump).ConfigureAwait(false);
      }

      if (failure != null)
        throw CreateCommandFailureException(
            effectiveContext,
            $"Streaming command failed ({failure}).",
            ErrorCodes.Driver.CommandExecutionFailed,
            new ErrorContext("StreamingCommand")
            {
              DriverId = effectiveContext.DriverId,
              Host = effectiveContext.Host,
              ExitCode = failureExitCode,
              StdErr = FormatTail(tail)
            });
    }

    /// <summary>
    /// Executes a streaming Podman command using the given driver context, pumping stdout and
    /// stderr concurrently and yielding each as a source-tagged <see cref="LogEntry"/> in
    /// arrival order. Both pipes are always drained (so a chatty stream on the suppressed side
    /// cannot deadlock the child), but only the streams selected by <paramref name="stdout"/> /
    /// <paramref name="stderr"/> are yielded. A short tail of recent lines is retained to
    /// enrich the exception message if the process exits non-zero.
    /// </summary>
    /// <param name="context">Driver context supplying host/sudo settings.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="stdout">Whether stdout lines are yielded.</param>
    /// <param name="stderr">Whether stderr lines are yielded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of source-tagged log entries, in arrival order.</returns>
    /// <exception cref="DriverException">The process exited with a non-zero code.</exception>
    protected async IAsyncEnumerable<LogEntry> ExecuteStreamingCommandWithSourcesAsync(
        DriverContext context,
        string arguments,
        bool stdout,
        bool stderr,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext, Logger);
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
      if (passwordForStdin != null)
        _ = await TryWriteStandardInputAsync(process, passwordForStdin, null, cancellationToken).ConfigureAwait(false);

      var channel = System.Threading.Channels.Channel.CreateBounded<LogEntry>(
          new System.Threading.Channels.BoundedChannelOptions(256)
          {
            SingleReader = true,
            SingleWriter = false,
            FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
          });
      var tail = new Queue<string>();
      var pump = PumpSourceStreamsAsync(process, channel.Writer, stdout, stderr, tail, cancellationToken);
      string failure = null;
      var failureExitCode = 0;

      try
      {
        await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
          yield return entry;

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
          failureExitCode = process.ExitCode;
          failure = $"exit code {process.ExitCode}{FormatTail(tail)}";
        }
      }
      finally
      {
        channel.Writer.TryComplete();
        KillProcessSafely(process, Logger);
        await ObserveQuietlyAsync(pump).ConfigureAwait(false);
      }

      if (failure != null)
        throw CreateCommandFailureException(
            effectiveContext,
            $"Streaming command failed ({failure}).",
            ErrorCodes.Driver.CommandExecutionFailed,
            new ErrorContext("StreamingCommand")
            {
              DriverId = effectiveContext.DriverId,
              Host = effectiveContext.Host,
              ExitCode = failureExitCode,
              StdErr = FormatTail(tail)
            });
    }

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

    private static async Task PumpSourceStreamsAsync(
        Process process,
        System.Threading.Channels.ChannelWriter<LogEntry> writer,
        bool stdout,
        bool stderr,
        Queue<string> tail,
        CancellationToken cancellationToken)
    {
      async Task PumpAsync(TextReader reader, LogStreamSource source, bool emit)
      {
        var lineReader = new BoundedLineReader(reader);
        string line;
        while ((line = await lineReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
          lock (tail)
            AddTail(tail, line);
          if (emit)
            await writer.WriteAsync(new LogEntry { Source = source, Line = line }, cancellationToken).ConfigureAwait(false);
        }
      }

      try
      {
        await Task.WhenAll(
            PumpAsync(process.StandardOutput, LogStreamSource.Stdout, stdout),
            PumpAsync(process.StandardError, LogStreamSource.Stderr, stderr)).ConfigureAwait(false);
        writer.TryComplete();
      }
      catch (Exception ex)
      {
        writer.TryComplete(ex);
      }
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
