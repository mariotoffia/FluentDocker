using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Podman.Cli
{
  /// <summary>
  /// Base class for Podman CLI driver components.
  /// Provides shared command execution functionality.
  /// </summary>
  public abstract partial class PodmanCliDriverBase
  {
    /// <summary>
    /// The Podman command executable name.
    /// </summary>
    protected const string PodmanCommand = "podman";

    private static readonly ConcurrentDictionary<string, byte> CertificateWarnings = new();

    private static readonly ConcurrentDictionary<string, byte> VerifyTlsWarnings = new();

    /// <summary>
    /// The driver context.
    /// </summary>
    protected DriverContext Context { get; private set; }

    /// <summary>
    /// Logger for this driver component. Category equals the concrete derived type's FQN.
    /// </summary>
    protected ILogger Logger { get; private set; } = NullLogger.Instance;

    /// <summary>
    /// The binary resolver for resolving Podman command paths.
    /// </summary>
    protected IPodmanBinaryResolver BinaryResolver { get; private set; }

    /// <summary>
    /// Null-safe enumeration source. The fluent builders null out empty collections before
    /// calling the driver, so every collection walked while building args must tolerate a null.
    /// Routing loops through this one helper fixes the NRE once for every argument builders emit.
    /// </summary>
    protected static IEnumerable<T> OrEmpty<T>(IEnumerable<T> source) => source ?? Enumerable.Empty<T>();

    /// <summary>
    /// Creates a new instance without a binary resolver.
    /// </summary>
    protected PodmanCliDriverBase()
    {
    }

    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    protected PodmanCliDriverBase(IPodmanBinaryResolver binaryResolver) => BinaryResolver = binaryResolver;

    /// <summary>
    /// Initializes the driver component with the given context.
    /// </summary>
    public virtual void Initialize(DriverContext context)
    {
      ArgumentNullException.ThrowIfNull(context);
      Context = context;
      Logger = context.LoggerFactory.CreateLogger(GetType());
    }

    /// <summary>
    /// Initializes the driver component with the given context and binary resolver.
    /// </summary>
    public virtual void Initialize(DriverContext context, IPodmanBinaryResolver binaryResolver)
    {
      ArgumentNullException.ThrowIfNull(context);
      ArgumentNullException.ThrowIfNull(binaryResolver);
      Context = context;
      BinaryResolver = binaryResolver;
      Logger = context.LoggerFactory.CreateLogger(GetType());
    }

    #region Global Args

    /// <summary>
    /// Builds global CLI flags from the driver context.
    /// Podman uses --url for the remote host. TLS settings are not supported via the
    /// Podman CLI, so <see cref="DriverContext.CertificatePath"/> and
    /// <see cref="DriverContext.VerifyTls"/> are ignored. Warning deduplication is
    /// process-wide and keyed by driver/host, so long-lived hosts see each warning once.
    /// </summary>
    /// <param name="context">The driver context (may be null).</param>
    /// <param name="logger">Optional logger used for one-time warnings about ignored settings.</param>
    /// <returns>A string of global flags to prepend to Podman commands, or empty string.</returns>
    public static string BuildGlobalArgs(DriverContext context, ILogger logger = null)
    {
      if (context == null)
        return "";

      if (!string.IsNullOrEmpty(context.CertificatePath))
        WarnCertificatePathIgnoredOnce(context, logger);

      if (context.VerifyTls.HasValue)
        WarnVerifyTlsIgnoredOnce(context, logger);

      if (string.IsNullOrEmpty(context.Host))
        return "";

      return $"--url {QuoteArgumentIfNeeded(context.Host)}";
    }

    private static void WarnCertificatePathIgnoredOnce(DriverContext context, ILogger logger)
    {
      if (logger == null)
        return;

      var key = context.DriverId ?? context.Host ?? context.CertificatePath;
      if (CertificateWarnings.TryAdd(key, 0))
        logger.LogWarning(
            "Podman CLI ignores DriverContext.CertificatePath because podman CLI does not expose Docker-style TLS certificate flags.");
    }

    private static void WarnVerifyTlsIgnoredOnce(DriverContext context, ILogger logger)
    {
      if (logger == null)
        return;

      var key = context.DriverId ?? context.Host ?? "default";
      if (VerifyTlsWarnings.TryAdd(key, 0))
        logger.LogWarning(
            "Podman CLI ignores DriverContext.VerifyTls because podman CLI has no Docker-style daemon TLS-verify flag; podman's --tls-verify is a per-command registry flag, not a connection setting.");
    }

    #endregion

    #region Command Execution

    /// <summary>
    /// Sanity cap on the bytes a single non-streaming Podman command may buffer for
    /// stdout/stderr. A pathological child cannot force unbounded memory growth: stdout
    /// fails the command on exceeding the cap, stderr is truncated (kept, with a marker).
    /// </summary>
    private const int MaxNonStreamingOutputBytes = 4 * 1024 * 1024;

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
    /// </summary>
    private (string BinaryPath, SudoMechanism Sudo, string SudoPassword) ResolveBinaryInfo()
        => ResolveBinaryInfo(Context);

    private (string BinaryPath, SudoMechanism Sudo, string SudoPassword) ResolveBinaryInfo(DriverContext context)
    {
      var contextSudo = context?.Sudo ?? SudoMechanism.None;
      var contextPassword = context?.SudoPassword;

      if (BinaryResolver == null)
        return (PodmanCommand, contextSudo, contextPassword);

      var binary = BinaryResolver.Resolve(PodmanCommand);
      return (binary.FqPath,
          contextSudo != SudoMechanism.None ? contextSudo : binary.Sudo,
          contextPassword ?? binary.SudoPassword);
    }

    /// <summary>
    /// Executes a Podman command asynchronously.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, CancellationToken cancellationToken)
        => await ExecuteCommandAsync((DriverContext)null, arguments, cancellationToken).ConfigureAwait(false);

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
    /// Executes a Podman command asynchronously with data piped to stdin.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, string stdinData, CancellationToken cancellationToken)
        => await ExecuteCommandAsync((DriverContext)null, arguments, stdinData, cancellationToken).ConfigureAwait(false);

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

    protected Task<SimpleCommandResult> ExecuteUnboundedCommandAsync(
        DriverContext context, string arguments, CancellationToken cancellationToken)
        => ExecuteUnboundedProcessAsync(context, arguments, cancellationToken);

    /// <summary>
    /// Executes a process asynchronously using direct stream reading
    /// to avoid event-based output race conditions.
    /// Handles sudo by setting the process FileName to "sudo" and passing the
    /// password via stdin (never on the command line).
    /// </summary>
    private static async Task<SimpleCommandResult> ExecuteProcessAsync(
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
        outputTask = ReadBoundedAsync(process.StandardOutput, MaxNonStreamingOutputBytes, linkedToken);
        errorTask = ReadBoundedTruncatingAsync(process.StandardError, MaxNonStreamingOutputBytes, linkedToken);

        var stdinFailure = needsStdin
            ? await TryWriteStandardInputAsync(process, passwordForStdin, stdinData, linkedToken).ConfigureAwait(false)
            : null;

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        // Ensure process has fully exited and get exit code.
        await process.WaitForExitAsync(linkedToken).ConfigureAwait(false);

        return new SimpleCommandResult
        {
          Success = process.ExitCode == 0 && stdinFailure == null,
          Output = output,
          Error = string.IsNullOrEmpty(error) && stdinFailure != null ? stdinFailure.Message : error,
          ExitCode = process.ExitCode
        };
      }
      catch (OperationCanceledException)
      {
        // Kill the child process on cancellation to prevent orphans.
        KillProcessSafely(process, null);

        await TryReadStringTaskAsync(outputTask).ConfigureAwait(false);
        await TryReadStringTaskAsync(errorTask).ConfigureAwait(false);

        // Distinguish caller-driven cancellation from the buffered-command timeout firing:
        // the caller's intent is rethrown as an OCE bound to the caller's token; a timeout
        // surfaces as a clear DriverException.
        cancellationToken.ThrowIfCancellationRequested();

        throw new DriverException(
            $"Podman CLI command timed out after {timeout.TotalSeconds:0}s.",
            ErrorCodes.General.Timeout);
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
        var output = await TryReadStringTaskAsync(outputTask).ConfigureAwait(false);
        var error = await TryReadStringTaskAsync(errorTask).ConfigureAwait(false);
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

    #region Error Context

    /// <summary>
    /// Creates an error context from a command result.
    /// </summary>
    protected static ErrorContext CreateErrorContext(
        DriverContext context, string operation, SimpleCommandResult result)
    {
      return new ErrorContext(operation)
      {
        DriverId = context.DriverId,
        Host = context.Host,
        ExitCode = result.ExitCode,
        StdOut = result.Output,
        StdErr = result.Error
      };
    }

    /// <summary>
    /// Creates an error context using the component's context.
    /// </summary>
    protected ErrorContext CreateErrorContext(string operation, SimpleCommandResult result)
    {
      return CreateErrorContext(Context, operation, result);
    }

    protected static string ErrorOrDefault(SimpleCommandResult result, string fallback)
    {
      return string.IsNullOrEmpty(result?.Error) ? fallback : result.Error;
    }

    protected static string MergeOutputAndError(string output, string error)
    {
      if (string.IsNullOrEmpty(output))
        return error ?? string.Empty;
      if (string.IsNullOrEmpty(error))
        return output;
      return output.EndsWith('\n') || error.StartsWith('\n') ? output + error : output + "\n" + error;
    }

    #endregion

    #region Process Lifecycle

    /// <summary>
    /// Builds the actual process FileName and Arguments for sudo-aware execution.
    /// The password is NEVER placed on the command line — it is returned separately
    /// for writing to stdin.
    /// </summary>
    private static (string FileName, string Arguments, string PasswordForStdin) BuildSudoCommand(
        string binaryPath, string arguments, SudoMechanism sudo, string sudoPassword)
    {
      return sudo switch
      {
        SudoMechanism.NoPassword => ("sudo", $"-n -- {QuoteArgumentIfNeeded(binaryPath)} {arguments}", null),
        SudoMechanism.Password => ("sudo", $"-S -- {QuoteArgumentIfNeeded(binaryPath)} {arguments}", sudoPassword),
        _ => (binaryPath, arguments, null)
      };
    }

    /// <summary>
    /// Safely kills a process if it is still running, suppressing any errors.
    /// </summary>
    private static void KillProcessSafely(Process process, ILogger logger = null)
    {
      if (process == null)
        return;

      try
      {
        if (!process.HasExited)
          process.Kill(entireProcessTree: true);
      }
      catch (Exception ex)
      {
        (logger ?? NullLogger.Instance).LogWarning(ex, "Process kill failed");
      }
    }

    #endregion

    #region Argument Quoting

    /// <summary>
    /// Quotes a command-line argument if it contains shell metacharacters or whitespace,
    /// using the shared CommandLineToArgvW-compatible quoting algorithm.
    /// </summary>
    protected static string QuoteArgumentIfNeeded(string argument)
    {
      return CommandLineQuoting.QuoteArgumentIfNeeded(argument);
    }

    protected static bool StartsWithDash(string value) =>
        !string.IsNullOrEmpty(value) && value[0] == '-';

    protected static string QuotePositionalArgument(string argument, string argumentName)
    {
      if (StartsWithDash(argument))
        throw new DriverException(
            $"{argumentName} must not start with '-' because Podman would parse it as an option.",
            ErrorCodes.General.InvalidArgument);
      return QuoteArgumentIfNeeded(argument);
    }

    #endregion
  }
}
