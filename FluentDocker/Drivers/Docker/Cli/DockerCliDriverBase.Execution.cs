using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli
{
  /// <summary>
  /// Command-execution half of <see cref="DockerCliDriverBase"/> (process spawning,
  /// bounded buffered reads, streaming, and attach). Split into its own partial file
  /// purely to keep each source file within the repository's 500-line limit.
  /// </summary>
  public abstract partial class DockerCliDriverBase
  {
    #region Command Execution

    /// <summary>
    /// Sanity cap (4 MiB) on the buffered stdout of a non-streaming Docker command.
    /// Normal model/system command output (e.g. <c>docker model ls --json</c>) is tiny;
    /// this only guards against pathological CLI output being buffered without limit
    /// (which would otherwise risk an out-of-memory condition). On exceeding the cap the
    /// command fails with a clear error rather than continuing to allocate. Streaming
    /// commands are unaffected — they are read line-by-line and never fully buffered.
    /// </summary>
    private const int MaxNonStreamingOutputBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Default wall-clock timeout applied to a buffered (non-streaming) Docker CLI command
    /// when the caller's <see cref="DriverContext.RequestTimeout"/> is not set. Without it a
    /// hung docker CLI / plugin / daemon call would block forever when the caller passes
    /// <see cref="CancellationToken.None"/>. Mirrors the Docker API driver's 5-minute request
    /// default. Streaming/attach paths are intentionally exempt (logs -f / events run forever).
    /// </summary>
    private static readonly TimeSpan DefaultBufferedCommandTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Resolves the buffered-command timeout from the driver context, falling back to
    /// <see cref="DefaultBufferedCommandTimeout"/> when no <see cref="DriverContext.RequestTimeout"/>
    /// is configured.
    /// </summary>
    private TimeSpan ResolveBufferedTimeout()
        => Context?.RequestTimeout ?? DefaultBufferedCommandTimeout;

    /// <summary>
    /// Resolves the binary info for the Docker command, extracting
    /// the binary path and sudo configuration separately for safe execution.
    /// </summary>
    private (string BinaryPath, SudoMechanism Sudo, string SudoPassword) ResolveBinaryInfo()
    {
      if (BinaryResolver == null)
        return (DockerCommand, SudoMechanism.None, null);

      var binary = BinaryResolver.Resolve(DockerCommand);
      return (binary.FqPath, binary.Sudo, binary.SudoPassword);
    }

    /// <summary>
    /// Executes a Docker command asynchronously.
    /// </summary>
    /// <param name="arguments">Command arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Command result</returns>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(string arguments, CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, null, sudo, sudoPassword, ResolveBufferedTimeout(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Docker command asynchronously with data piped to stdin.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, string stdinData, CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, stdinData, sudo, sudoPassword, ResolveBufferedTimeout(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Docker command asynchronously with additional environment variables.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments,
        IDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, environment, null, sudo, sudoPassword, ResolveBufferedTimeout(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Docker command asynchronously with an explicit buffered timeout.
    /// Identical to <see cref="ExecuteCommandAsync(string, CancellationToken)"/> but uses the
    /// supplied <paramref name="timeout"/> instead of the resolved default. Pass
    /// <see cref="Timeout.InfiniteTimeSpan"/> to bound only by the caller token.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, null, sudo, sudoPassword, timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// For inherently long / unbounded-by-design Docker operations
    /// (pull/build/push/wait/load/save/stop -t …) that must honor ONLY caller cancellation;
    /// the default buffered timeout would falsely abort them.
    /// </summary>
    protected Task<SimpleCommandResult> ExecuteUnboundedCommandAsync(string arguments, CancellationToken cancellationToken)
        => ExecuteCommandAsync(arguments, Timeout.InfiniteTimeSpan, cancellationToken);

    /// <summary>
    /// Executes a process asynchronously using direct stream reading
    /// to avoid event-based output race conditions.
    /// Handles sudo by setting the process FileName to "sudo" and passing the
    /// password via stdin (never on the command line).
    /// </summary>
    private static async Task<SimpleCommandResult> ExecuteProcessAsync(
        string fileName, string arguments,
        IDictionary<string, string> environment,
        string stdinData,
        SudoMechanism sudo, string sudoPassword,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
      // Build the actual process command based on sudo mechanism.
      // The password is NEVER placed on the command line.
      var (processFileName, processArguments, passwordForStdin) =
          BuildSudoCommand(fileName, arguments, sudo, sudoPassword);

      var needsStdin = stdinData != null || passwordForStdin != null;

      // Bound the wall-clock time of a buffered command: link the caller token with a timeout
      // so a hung docker CLI/plugin/daemon call cannot block forever (the caller frequently
      // passes CancellationToken.None). The linked token drives the stdout/stderr reads and
      // WaitForExitAsync below.
      using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      if (timeout != Timeout.InfiniteTimeSpan)
        linked.CancelAfter(timeout);
      var linkedToken = linked.Token;

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
            RedirectStandardInput = needsStdin,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
          }
        };

        if (environment != null)
        {
          foreach (var kvp in environment)
            process.StartInfo.Environment[kvp.Key] = kvp.Value;
        }

        process.Start();

        if (needsStdin)
        {
          // Write sudo password first (if any), then caller data.
          if (passwordForStdin != null)
            await process.StandardInput.WriteLineAsync(passwordForStdin.AsMemory(), linkedToken).ConfigureAwait(false);

          if (stdinData != null)
            await process.StandardInput.WriteAsync(stdinData.AsMemory(), linkedToken).ConfigureAwait(false);

          process.StandardInput.Close();
        }

        // Read stdout and stderr concurrently to avoid deadlock
        // when either pipe buffer fills up. Both streams are bounded by a sanity cap so a
        // pathological child cannot force unbounded buffering; stdout fails the command on
        // exceeding the cap, while stderr (the error message itself) is truncated and kept.
        var outputTask = ReadBoundedAsync(process.StandardOutput, MaxNonStreamingOutputBytes, linkedToken);
        var errorTask = ReadBoundedTruncatingAsync(process.StandardError, MaxNonStreamingOutputBytes, linkedToken);

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        // Ensure process has fully exited and get exit code.
        await process.WaitForExitAsync(linkedToken).ConfigureAwait(false);

        return new SimpleCommandResult
        {
          Success = process.ExitCode == 0,
          Output = output,
          Error = error,
          ExitCode = process.ExitCode
        };
      }
      catch (OperationCanceledException)
      {
        // Kill the child process on cancellation to prevent orphans.
        KillProcessSafely(process);

        // Distinguish caller-driven cancellation from the buffered-command timeout firing:
        // the caller's intent is rethrown as an OCE bound to the caller's token; a timeout
        // surfaces as a clear DriverException.
        cancellationToken.ThrowIfCancellationRequested();

        throw new DriverException(
            $"Docker CLI command timed out after {timeout.TotalSeconds:0}s.",
            ErrorCodes.General.Timeout);
      }
      catch (Exception ex)
      {
        try
        { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
        catch { /* best effort — process may have exited between the check and the kill */ }
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
    /// Executes a streaming Docker command asynchronously.
    /// </summary>
    /// <param name="arguments">Command arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of output lines</returns>
    protected async IAsyncEnumerable<string> ExecuteStreamingCommandAsync(string arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
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

      // Drain stderr concurrently so a chatty child cannot deadlock by filling the
      // stderr pipe buffer while we only read stdout.
      var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
      var reader = process.StandardOutput;
      string failure = null;

      try
      {
        string line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
          yield return line;

        // Stdout reached EOF — wait for the process and surface a non-zero exit as a
        // failure rather than ending the stream silently (a failed pull/logs must throw).
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

      try
      {
        await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
          yield return line;

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
          failure = $"exit code {process.ExitCode}";
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
        throw new DriverException($"Streaming command failed ({failure}).", ErrorCodes.Driver.CommandExecutionFailed);
    }

    /// <summary>
    /// Reads stdout and stderr concurrently, writing every line to <paramref name="writer"/>
    /// in arrival order, then completes the writer when both streams reach EOF.
    /// </summary>
    private static async Task PumpBothStreamsAsync(
        Process process, System.Threading.Channels.ChannelWriter<string> writer, CancellationToken cancellationToken)
    {
      async Task PumpAsync(TextReader reader)
      {
        string line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
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

    /// <summary>Awaits a task, swallowing any fault — used to observe a best-effort
    /// background read (e.g. stderr drain) when a stream is torn down early.</summary>
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

    #endregion
  }
}
