using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli
{
  /// <summary>
  /// Command-execution half of <see cref="PodmanCliDriverBase"/> (buffered/bounded process
  /// spawning and reads). Split into its own partial file purely to keep each source file
  /// within the repository's 500-line limit.
  /// </summary>
  public abstract partial class PodmanCliDriverBase
  {
    #region Command Execution

    /// <summary>
    /// Sanity cap (64 MiB) on buffered stdout of a non-streaming Podman command. Large hosts
    /// can produce multi-MiB JSON from <c>ps -a --format json</c> / <c>inspect</c> / <c>images</c>;
    /// those outputs must fail above a high ceiling, not at a low one that breaks parity with the
    /// Docker CLI base. Streaming commands are unaffected (read line-by-line). Overridable so tests
    /// can exercise the bounded-read failure path without generating 64 MiB of output.
    /// </summary>
    protected virtual int MaxNonStreamingOutputBytes => 64 * 1024 * 1024;

    /// <summary>Cap (4 MiB) on buffered stderr; truncated (kept, with a marker), never fails the command.</summary>
    private const int MaxNonStreamingErrorBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Default wall-clock timeout applied to a buffered (non-streaming) Podman CLI command
    /// when the caller's <see cref="DriverContext.RequestTimeout"/> is not set. Without it a
    /// hung <c>podman</c> CLI call, a stalled machine SSH, or a stopped VM would block forever
    /// when the caller passes <see cref="CancellationToken.None"/>. Streaming/attach paths are
    /// intentionally exempt (logs -f / events run forever).
    /// </summary>
    private static readonly TimeSpan DefaultBufferedCommandTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Resolves the buffered-command timeout from the driver context, falling back to
    /// <see cref="DefaultBufferedCommandTimeout"/> when no <see cref="DriverContext.RequestTimeout"/>
    /// is configured.
    /// </summary>
    private static TimeSpan ResolveBufferedTimeout(DriverContext context)
        => context?.RequestTimeout ?? DefaultBufferedCommandTimeout;

    /// <summary>
    /// Resolves the binary info for the Podman command, extracting
    /// the binary path and sudo configuration separately for safe execution.
    /// A per-operation <see cref="DriverContext.BinaryName"/>/<see cref="DriverContext.SearchPaths"/>
    /// differing from the component's own context resolves a one-shot binary for this call —
    /// the merged values are honored, not silently ignored in favor of the pack-init resolver.
    /// </summary>
    private (string BinaryPath, SudoMechanism Sudo, string SudoPassword) ResolveBinaryInfo()
        => ResolveBinaryInfo(Context);

    private (string BinaryPath, SudoMechanism Sudo, string SudoPassword) ResolveBinaryInfo(DriverContext context)
    {
      var contextSudo = context?.Sudo ?? SudoMechanism.None;
      var contextPassword = context?.SudoPassword;

      if (HasPerOperationBinaryOverride(context))
      {
        var overrideResolver = new PodmanBinariesResolver(new PodmanBinaryConfiguration
        {
          Sudo = contextSudo,
          SudoPassword = contextPassword,
          DefaultShell = context.DefaultShell,
          BinaryName = context.BinaryName,
          SearchPaths = context.SearchPaths
        });
        var overrideBinary = overrideResolver.Resolve(
            string.IsNullOrWhiteSpace(context.BinaryName) ? PodmanCommand : context.BinaryName);
        return (overrideBinary.FqPath,
            contextSudo != SudoMechanism.None ? contextSudo : overrideBinary.Sudo,
            contextPassword ?? overrideBinary.SudoPassword);
      }

      if (BinaryResolver == null)
        return (PodmanCommand, contextSudo, contextPassword);

      var binary = BinaryResolver.Resolve(PodmanCommand);
      return (binary.FqPath,
          contextSudo != SudoMechanism.None ? contextSudo : binary.Sudo,
          contextPassword ?? binary.SudoPassword);
    }

    /// <summary>
    /// True when the merged per-operation context carries a binary name or search-path set
    /// that differs from the component's initialization context (i.e. the caller overrode
    /// them for this call). String/reference comparison suffices because
    /// <see cref="CreateEffectiveContext"/> passes component values through unchanged.
    /// </summary>
    private bool HasPerOperationBinaryOverride(DriverContext context)
    {
      if (context == null || BinaryResolver == null)
        return false;

      var component = Context;
      var binaryDiffers = !string.IsNullOrWhiteSpace(context.BinaryName)
          && !string.Equals(context.BinaryName, component?.BinaryName, StringComparison.Ordinal);
      var pathsDiffer = context.SearchPaths is { Length: > 0 }
          && !ReferenceEquals(context.SearchPaths, component?.SearchPaths);
      return binaryDiffers || pathsDiffer;
    }

    /// <summary>
    /// Executes a Podman command asynchronously.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, CancellationToken cancellationToken)
        => await ExecuteCommandAsync((DriverContext)null, arguments, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Executes a Podman command asynchronously using the given driver context: spawns the
    /// process, buffers stdout up to <see cref="MaxNonStreamingOutputBytes"/> (failing the
    /// command when exceeded) and stderr up to a separate 4 MiB cap that truncates with a
    /// marker but never fails the command, and waits for exit or the buffered timeout.
    /// </summary>
    /// <param name="context">Driver context supplying host/sudo settings and timeout.</param>
    /// <param name="arguments">Command arguments (without the podman binary name).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The command result.</returns>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        DriverContext context, string arguments, CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext, Logger);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, sudo, sudoPassword, ResolveBufferedTimeout(effectiveContext), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Podman command asynchronously with an explicit buffered timeout.
    /// Identical to <see cref="ExecuteCommandAsync(string, CancellationToken)"/> but uses the
    /// supplied <paramref name="timeout"/> instead of the resolved default. Pass
    /// <see cref="Timeout.InfiniteTimeSpan"/> to bound only by the caller token.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, TimeSpan timeout, CancellationToken cancellationToken)
        => await ExecuteCommandAsync((DriverContext)null, arguments, timeout, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Executes a Podman command asynchronously using the given driver context and an explicit
    /// buffered timeout instead of the context-resolved default.
    /// </summary>
    /// <param name="context">Driver context supplying host/sudo settings.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="timeout">
    /// Wall-clock timeout; pass <see cref="Timeout.InfiniteTimeSpan"/> to bound only by
    /// <paramref name="cancellationToken"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The command result.</returns>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        DriverContext context, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext, Logger);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, sudo, sudoPassword, timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Podman command asynchronously with data piped to stdin.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, string stdinData, CancellationToken cancellationToken)
        => await ExecuteCommandAsync((DriverContext)null, arguments, stdinData, cancellationToken).ConfigureAwait(false);

    /// <summary>Executes a Podman command asynchronously with data piped to stdin, using the given driver context.</summary>
    /// <param name="context">Driver context supplying host/sudo settings and timeout.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="stdinData">Data written to the process's standard input after it starts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The command result.</returns>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        DriverContext context, string arguments, string stdinData, CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext, Logger);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, stdinData, sudo, sudoPassword, ResolveBufferedTimeout(effectiveContext), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// For inherently long / unbounded-by-design Podman operations
    /// (pull/build/push/wait/load/save/stop -t/machine init/start …) that must honor ONLY
    /// caller cancellation; the default buffered timeout would falsely abort them. Unlike the
    /// bounded path this STREAMS stdout/stderr into bounded rolling tails (see
    /// <see cref="ExecuteUnboundedProcessAsync"/>) so a verbose-but-successful op is not failed
    /// at the 4 MiB buffered cap.
    /// </summary>
    protected Task<SimpleCommandResult> ExecuteUnboundedCommandAsync(string arguments, CancellationToken cancellationToken)
        => ExecuteUnboundedCommandAsync((DriverContext)null, arguments, cancellationToken);

    /// <summary>
    /// Executes an unbounded Podman command using the given driver context: honors only
    /// <paramref name="cancellationToken"/>, never the buffered-command default timeout, and
    /// streams stdout/stderr into bounded rolling tails via <see cref="ExecuteUnboundedProcessAsync"/>.
    /// </summary>
    /// <param name="context">Driver context supplying host/sudo settings.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The command result.</returns>
    protected Task<SimpleCommandResult> ExecuteUnboundedCommandAsync(
        DriverContext context, string arguments, CancellationToken cancellationToken)
        => ExecuteUnboundedProcessAsync(context, arguments, cancellationToken);

    /// <summary>
    /// Executes a process asynchronously using direct stream reading
    /// to avoid event-based output race conditions.
    /// Handles sudo by setting the process FileName to "sudo" and passing the
    /// password via stdin (never on the command line).
    /// </summary>
    // Instance (not static) so the overridable MaxNonStreamingOutputBytes cap is readable.
    private async Task<SimpleCommandResult> ExecuteProcessAsync(
        string fileName, string arguments,
        string stdinData,
        SudoMechanism sudo, string sudoPassword,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
      var (processFileName, processArguments, passwordForStdin) =
          BuildSudoCommand(fileName, arguments, sudo, sudoPassword);

      var needsStdin = stdinData != null || passwordForStdin != null;

      // Bound the wall-clock time of a buffered command: link the caller token with a timeout
      // so a hung podman CLI / machine SSH / stopped VM cannot block forever (the caller
      // frequently passes CancellationToken.None). The linked token drives the stdout/stderr
      // reads and WaitForExitAsync below.
      using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      if (timeout != Timeout.InfiniteTimeSpan)
        linked.CancelAfter(timeout);
      var linkedToken = linked.Token;

      Process process = null;
      Task<string> outputTask = null;
      Task<string> errorTask = null;
      var outputSink = new StringBuilder();
      var errorSink = new StringBuilder();
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

        // Intentionally raw: only streaming/unbounded/attach need StartProcessOrThrow; this buffered path catches start failures below and returns Fail.
        process.Start();

        // Always redirect stdin and close it when the command needs none, so a child that reads
        // stdin gets EOF instead of inheriting (and blocking on) this process's stdin.
        if (!needsStdin)
          process.StandardInput.Close();

        // Read stdout and stderr concurrently to avoid deadlock when either pipe buffer fills
        // up. Both streams are bounded by a sanity cap so a pathological child cannot force
        // unbounded buffering; stdout fails the command on exceeding the cap, while stderr
        // (the error message itself) is truncated and kept.
        outputTask = ReadBoundedAsync(process.StandardOutput, MaxNonStreamingOutputBytes, linkedToken, outputSink);
        errorTask = ReadBoundedTruncatingAsync(process.StandardError, MaxNonStreamingErrorBytes, linkedToken, errorSink);

        var stdinFailure = needsStdin
            ? await TryWriteStandardInputAsync(process, passwordForStdin, stdinData, linkedToken).ConfigureAwait(false)
            : null;

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        // Ensure process has fully exited and get exit code.
        await process.WaitForExitAsync(linkedToken).ConfigureAwait(false);

        return new SimpleCommandResult
        {
          // The exit code is authoritative: a broken-pipe stdin write against a child that
          // exited 0 (e.g. `podman login` succeeding from cached credentials and closing
          // stdin without reading it) is a success — not a contradictory Success=false with
          // ExitCode=0. The stdin failure only enriches the error text for genuine failures
          // (matching the Docker CLI driver's semantics).
          Success = process.ExitCode == 0,
          Output = output,
          Error = string.IsNullOrEmpty(error) && stdinFailure != null ? stdinFailure.Message : error,
          ExitCode = process.ExitCode
        };
      }
      catch (OperationCanceledException)
      {
        // Kill the child process on cancellation to prevent orphans.
        KillProcessSafely(process, null);

        // Drain the readers; keep what was captured so the timeout is diagnosable. A
        // cancelled reader yields null from its task — the caller-owned sink still holds
        // the partials accumulated before cancellation (race-free after the await).
        var partialOutput = await TryReadStringTaskAsync(outputTask).ConfigureAwait(false)
            ?? SinkSnapshot(outputSink);
        var partialError = await TryReadStringTaskAsync(errorTask).ConfigureAwait(false)
            ?? SinkSnapshot(errorSink);

        // Distinguish caller-driven cancellation from the buffered-command timeout firing:
        // the caller's intent is rethrown as an OCE bound to the caller's token; a timeout
        // surfaces as a clear DriverException.
        cancellationToken.ThrowIfCancellationRequested();

        throw new DriverException(
            $"Podman CLI command timed out after {timeout.TotalSeconds:0}s.",
            ErrorCodes.General.Timeout,
            new ErrorContext("BufferedCommand")
            {
              ExitCode = GetExitCodeOrDefault(process),
              StdOut = DiagnosticTail(partialOutput),
              StdErr = DiagnosticTail(partialError)
            });
      }
      catch (Exception ex)
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
        var output = await TryReadStringTaskAsync(outputTask).ConfigureAwait(false)
            ?? SinkSnapshot(outputSink);
        var error = await TryReadStringTaskAsync(errorTask).ConfigureAwait(false)
            ?? SinkSnapshot(errorSink);
        return new SimpleCommandResult
        {
          Success = false,
          Output = output,
          Error = string.IsNullOrEmpty(error) ? ex.Message : $"{ex.Message}\n{error}",
          ExitCode = GetExitCodeOrDefault(process)
        };
      }
      finally
      {
        process?.Dispose();
      }
    }

    #endregion
  }
}
