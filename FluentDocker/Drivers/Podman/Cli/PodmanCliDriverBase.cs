using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
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
  public abstract class PodmanCliDriverBase
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

      return $"--url {context.Host}";
    }

    #endregion

    #region Command Execution

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
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, sudo, sudoPassword, cancellationToken).ConfigureAwait(false);
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
      return await ExecuteProcessAsync(binaryPath, fullArgs, stdinData, sudo, sudoPassword, cancellationToken).ConfigureAwait(false);
    }

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
        CancellationToken cancellationToken)
    {
      var (processFileName, processArguments, passwordForStdin) =
          BuildSudoCommand(fileName, arguments, sudo, sudoPassword);

      var needsStdin = stdinData != null || passwordForStdin != null;

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
            CreateNoWindow = true
          }
        };

        process.Start();

        if (needsStdin)
        {
          if (passwordForStdin != null)
            await process.StandardInput.WriteLineAsync(passwordForStdin).ConfigureAwait(false);

          if (stdinData != null)
            await process.StandardInput.WriteAsync(stdinData).ConfigureAwait(false);

          process.StandardInput.Close();
        }

        // Read stdout and stderr concurrently to avoid deadlock
        // when either pipe buffer fills up.
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        // Ensure process has fully exited and get exit code.
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

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
        KillProcessSafely(process, null);
        throw;
      }
      catch (Exception ex)
      {
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
    /// Executes a streaming Podman command asynchronously.
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
          CreateNoWindow = true
        }
      };

      process.Start();

      if (passwordForStdin != null)
      {
        await process.StandardInput.WriteLineAsync(passwordForStdin).ConfigureAwait(false);
        process.StandardInput.Close();
      }

      var reader = process.StandardOutput;

      try
      {
        while (!cancellationToken.IsCancellationRequested)
        {
          var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
          if (line == null)
            break;

          yield return line;
        }
      }
      finally
      {
        KillProcessSafely(process, Logger);
      }
    }

    /// <summary>
    /// Starts a long-running attach process with stdin/stdout/stderr redirected.
    /// </summary>
    protected AttachResult ExecuteAttachProcess(string arguments)
    {
      var (binaryPath, sudo, _) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";

      var (processFileName, processArguments, _) =
          BuildSudoCommand(binaryPath, fullArgs, sudo, null);

      var process = new Process
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = processFileName,
          Arguments = processArguments,
          RedirectStandardInput = true,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        }
      };

      process.Start();

      return new AttachResult
      {
        InputStream = process.StandardInput.BaseStream,
        OutputStream = process.StandardOutput.BaseStream,
        ErrorStream = process.StandardError.BaseStream,
        IsConnected = true,
        AttachedProcess = process
      };
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
