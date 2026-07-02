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
    /// Podman uses --url for remote host. TLS certificate flags are not
    /// supported via the Podman CLI, so <see cref="DriverContext.CertificatePath"/>
    /// is ignored.
    /// </summary>
    /// <param name="context">The driver context (may be null).</param>
    /// <returns>A string of global flags to prepend to Podman commands, or empty string.</returns>
    public static string BuildGlobalArgs(DriverContext context)
    {
      if (context == null || string.IsNullOrEmpty(context.Host))
        return "";

      return $"--url {QuoteArgumentIfNeeded(context.Host)}";
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
    private TimeSpan ResolveBufferedTimeout()
        => Context?.RequestTimeout ?? DefaultBufferedCommandTimeout;

    /// <summary>
    /// Resolves the binary info for the Podman command, extracting
    /// the binary path and sudo configuration separately for safe execution.
    /// </summary>
    private (string BinaryPath, SudoMechanism Sudo, string SudoPassword) ResolveBinaryInfo()
    {
      if (BinaryResolver == null)
        return (PodmanCommand, SudoMechanism.None, null);

      var binary = BinaryResolver.Resolve(PodmanCommand);
      return (binary.FqPath, binary.Sudo, binary.SudoPassword);
    }

    /// <summary>
    /// Executes a Podman command asynchronously.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, sudo, sudoPassword, ResolveBufferedTimeout(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Podman command asynchronously with data piped to stdin.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, string stdinData, CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, stdinData, sudo, sudoPassword, ResolveBufferedTimeout(), cancellationToken).ConfigureAwait(false);
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
        => ExecuteUnboundedProcessAsync(arguments, cancellationToken);

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

        process.Start();

        if (needsStdin)
        {
          if (passwordForStdin != null)
            await process.StandardInput.WriteLineAsync(passwordForStdin.AsMemory(), linkedToken).ConfigureAwait(false);

          if (stdinData != null)
            await process.StandardInput.WriteAsync(stdinData.AsMemory(), linkedToken).ConfigureAwait(false);

          process.StandardInput.Close();
        }

        // Read stdout and stderr concurrently to avoid deadlock when either pipe buffer fills
        // up. Both streams are bounded by a sanity cap so a pathological child cannot force
        // unbounded buffering; stdout fails the command on exceeding the cap, while stderr
        // (the error message itself) is truncated and kept.
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
        KillProcessSafely(process, null);

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
        // Kill the child on any non-cancellation failure (e.g. stdout exceeded the cap, so
        // ReadBoundedAsync threw) so a still-writing podman process is not orphaned; the
        // finally below only releases handles via Dispose, which does not stop the process.
        KillProcessSafely(process, null);

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
        SudoMechanism.NoPassword => ("sudo", $"{binaryPath} {arguments}", null),
        SudoMechanism.Password => ("sudo", $"-S {binaryPath} {arguments}", sudoPassword),
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

    private static readonly System.Buffers.SearchValues<char> ShellMetaCharacters =
        System.Buffers.SearchValues.Create([' ', '\t', ';', '&', '|', '>', '<', '"', '\'', '$', '`', '!', '*', '?']);

    /// <summary>
    /// Quotes a command-line argument if it contains shell metacharacters or whitespace.
    /// Escapes backslashes and double quotes within the argument.
    /// </summary>
    protected static string QuoteArgumentIfNeeded(string argument)
    {
      if (string.IsNullOrEmpty(argument))
        return "\"\"";

      var needsQuoting = argument.AsSpan().IndexOfAny(ShellMetaCharacters) >= 0;
      if (!needsQuoting)
        return argument;

      var escaped = argument.Replace("\\", "\\\\").Replace("\"", "\\\"");
      return $"\"{escaped}\"";
    }

    #endregion
  }
}
