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
    /// Sanity cap (64 MiB) on buffered stdout of a non-streaming Docker command.
    /// Large hosts can produce multi-MiB JSON from list/inspect reads; those outputs must
    /// fail above a high ceiling, not truncate into corrupt JSON. Streaming commands are
    /// unaffected — they are read line-by-line and never fully buffered. Overridable so tests
    /// can exercise the bounded-read failure path without generating 64 MiB of output.
    /// </summary>
    protected virtual int MaxNonStreamingOutputBytes => 64 * 1024 * 1024;
    private const int MaxNonStreamingErrorBytes = 4 * 1024 * 1024;

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
    protected static TimeSpan ResolveBufferedTimeout(DriverContext context)
        => context?.RequestTimeout ?? DefaultBufferedCommandTimeout;

    /// <summary>
    /// Resolves the binary info for the Docker command, extracting
    /// the binary path and sudo configuration separately for safe execution.
    /// </summary>
    private (string BinaryPath, SudoMechanism Sudo, string SudoPassword) ResolveBinaryInfo(DriverContext context)
    {
      var contextSudo = context?.Sudo ?? SudoMechanism.None;
      var contextPassword = context?.SudoPassword;

      if (BinaryResolver == null)
        return (DockerCommand, contextSudo, contextPassword);

      var binary = BinaryResolver.Resolve(DockerCommand);
      return (binary.FqPath,
          contextSudo != SudoMechanism.None ? contextSudo : binary.Sudo,
          contextPassword ?? binary.SudoPassword);
    }

    /// <summary>
    /// Executes a Docker command asynchronously.
    /// </summary>
    /// <param name="arguments">Command arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Command result</returns>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(string arguments, CancellationToken cancellationToken)
        => await ExecuteCommandAsync((DriverContext)null, arguments, cancellationToken).ConfigureAwait(false);

    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        DriverContext context, string arguments, CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, null, sudo, sudoPassword, ResolveBufferedTimeout(effectiveContext), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Docker command asynchronously with data piped to stdin.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, string stdinData, CancellationToken cancellationToken)
        => await ExecuteCommandAsync((DriverContext)null, arguments, stdinData, cancellationToken).ConfigureAwait(false);

    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        DriverContext context, string arguments, string stdinData, CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, stdinData, sudo, sudoPassword, ResolveBufferedTimeout(effectiveContext), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Docker command asynchronously with additional environment variables.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments,
        IDictionary<string, string> environment,
        CancellationToken cancellationToken)
        => await ExecuteCommandAsync((DriverContext)null, arguments, environment, cancellationToken).ConfigureAwait(false);

    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        DriverContext context,
        string arguments,
        IDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, environment, null, sudo, sudoPassword, ResolveBufferedTimeout(effectiveContext), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Docker command asynchronously with an explicit buffered timeout.
    /// Identical to <see cref="ExecuteCommandAsync(string, CancellationToken)"/> but uses the
    /// supplied <paramref name="timeout"/> instead of the resolved default. Pass
    /// <see cref="Timeout.InfiniteTimeSpan"/> to bound only by the caller token.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, TimeSpan timeout, CancellationToken cancellationToken)
        => await ExecuteCommandAsync((DriverContext)null, arguments, timeout, cancellationToken).ConfigureAwait(false);

    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        DriverContext context, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, null, sudo, sudoPassword, timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// For inherently long / unbounded-by-design Docker operations
    /// (pull/build/push/wait/load/save/stop -t …) that must honor ONLY caller cancellation;
    /// the default buffered timeout would falsely abort them.
    /// </summary>
    protected Task<SimpleCommandResult> ExecuteUnboundedCommandAsync(string arguments, CancellationToken cancellationToken)
        => ExecuteUnboundedProcessAsync(null, arguments, null, cancellationToken);

    protected Task<SimpleCommandResult> ExecuteUnboundedCommandAsync(
        DriverContext context, string arguments, CancellationToken cancellationToken)
        => ExecuteUnboundedProcessAsync(context, arguments, null, cancellationToken);

    protected Task<SimpleCommandResult> ExecuteUnboundedCommandAsync(
        DriverContext context,
        string arguments,
        IDictionary<string, string> environment,
        CancellationToken cancellationToken)
        => ExecuteUnboundedProcessAsync(context, arguments, environment, cancellationToken);

    /// <summary>
    /// Executes a process asynchronously using direct stream reading
    /// to avoid event-based output race conditions.
    /// Handles sudo by setting the process FileName to "sudo" and passing the
    /// password via stdin (never on the command line).
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteProcessAsync(
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
      Task<string> outputTask = null;
      Task<string> errorTask = null;
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
            StandardErrorEncoding = Encoding.UTF8
          }
        };

        process.StartInfo.StandardInputEncoding = Utf8NoBom;

        if (environment != null)
        {
          foreach (var kvp in environment)
            process.StartInfo.Environment[kvp.Key] = kvp.Value;
        }

        StartProcessOrThrow(process, fileName);
        processStarted = true;
        if (!needsStdin)
          process.StandardInput.Close();

        // Start readers before writing stdin so a child that immediately writes enough
        // output cannot deadlock while this side is still feeding stdin.
        outputTask = ReadBoundedAsync(process.StandardOutput, MaxNonStreamingOutputBytes, linkedToken);
        errorTask = ReadBoundedTruncatingAsync(process.StandardError, MaxNonStreamingErrorBytes, linkedToken);

        var stdinFailure = needsStdin
            ? await TryWriteStandardInputAsync(process, passwordForStdin, stdinData, linkedToken).ConfigureAwait(false)
            : null;

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        // Ensure process has fully exited and get exit code.
        await process.WaitForExitAsync(linkedToken).ConfigureAwait(false);

        return new SimpleCommandResult
        {
          Success = process.ExitCode == 0,
          Output = output,
          Error = string.IsNullOrEmpty(error) && stdinFailure != null ? stdinFailure.Message : error,
          ExitCode = process.ExitCode
        };
      }
      catch (OperationCanceledException)
      {
        // Kill the child process on cancellation to prevent orphans.
        KillProcessSafely(process);

        // Drain the readers so `finally` doesn't dispose the process under an in-flight read.
        await TryReadStringTaskAsync(outputTask).ConfigureAwait(false);
        await TryReadStringTaskAsync(errorTask).ConfigureAwait(false);

        // Distinguish caller-driven cancellation from the buffered-command timeout firing:
        // the caller's intent is rethrown as an OCE bound to the caller's token; a timeout
        // surfaces as a clear DriverException.
        cancellationToken.ThrowIfCancellationRequested();

        throw new DriverException(
            $"Docker CLI command timed out after {FormatInvariant(timeout.TotalSeconds, "0")}s.",
            ErrorCodes.General.Timeout);
      }
      catch (Exception ex) when (processStarted || ex is not DriverException)
      {
        try
        {
          if (process is { HasExited: false })
            await Task.WhenAny(process.WaitForExitAsync(CancellationToken.None), Task.Delay(100, CancellationToken.None)).ConfigureAwait(false);
          if (process is { HasExited: false })
            process.Kill(entireProcessTree: true);
          if (process is { HasExited: false })
            await Task.WhenAny(process.WaitForExitAsync(CancellationToken.None), Task.Delay(2000, CancellationToken.None)).ConfigureAwait(false);
        }
        catch { /* best effort — process may have exited between the check and the kill */ }
        var output = await TryReadStringTaskAsync(outputTask).ConfigureAwait(false);
        var error = await TryReadStringTaskAsync(errorTask).ConfigureAwait(false);
        return new SimpleCommandResult
        {
          Success = false,
          Output = output,
          // The exception (e.g. the output-cap DriverException) is the primary failure;
          // stderr may race in SIGPIPE noise from the killed child, so always keep both.
          Error = string.IsNullOrEmpty(error) ? ex.Message : $"{ex.Message}\n{error}",
          ExitCode = GetExitCodeOrDefault(process)
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
      await foreach (var line in ExecuteStreamingCommandAsync((DriverContext)null, arguments, cancellationToken).ConfigureAwait(false))
        yield return line;
    }

    protected async IAsyncEnumerable<string> ExecuteStreamingCommandAsync(
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

      // Drain stderr concurrently so a chatty child cannot deadlock by filling the
      // stderr pipe buffer while we only read stdout.
      var errorTask = ReadBoundedTruncatingAsync(process.StandardError, MaxNonStreamingErrorBytes, cancellationToken);
      var lineReader = new BoundedLineReader(process.StandardOutput);
      string failure = null;

      try
      {
        // Write the sudo password inside the guarded region: if sudo exits immediately the
        // write throws a broken-pipe IOException, and it must still reach KillProcessSafely in
        // finally instead of orphaning the child (DCLI-MAJ-1). TryWrite swallows + closes stdin.
        if (passwordForStdin != null)
          _ = await TryWriteStandardInputAsync(process, passwordForStdin, null, cancellationToken)
              .ConfigureAwait(false);

        string line;
        while ((line = await lineReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
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
          failure = $"exit code {FormatInvariant(process.ExitCode)}{(trimmed.Length == 0 ? string.Empty : $": {trimmed}")}";
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
      await foreach (var line in ExecuteStreamingCommandWithProgressAsync((DriverContext)null, arguments, cancellationToken).ConfigureAwait(false))
        yield return line;
    }

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

    #endregion
  }
}
