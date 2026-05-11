using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Docker.Cli
{
  /// <summary>
  /// Base class for Docker CLI driver components.
  /// Provides shared command execution functionality.
  /// </summary>
  public abstract class DockerCliDriverBase
  {
    /// <summary>
    /// The Docker command executable name.
    /// </summary>
    protected const string DockerCommand = "docker";

    /// <summary>
    /// The driver context.
    /// </summary>
    protected DriverContext Context { get; private set; }

    /// <summary>
    /// Logger for this driver component. Category equals the concrete derived type's FQN.
    /// Defaults to <see cref="NullLogger.Instance"/> until <see cref="Initialize(DriverContext)"/> is called.
    /// </summary>
    protected ILogger Logger { get; private set; } = NullLogger.Instance;

    /// <summary>
    /// The binary resolver for resolving Docker command paths.
    /// </summary>
    protected IBinaryResolver BinaryResolver { get; private set; }

    /// <summary>
    /// Creates a new instance without a binary resolver.
    /// </summary>
    protected DockerCliDriverBase()
    {
    }

    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    /// <param name="binaryResolver">The binary resolver to use.</param>
    protected DockerCliDriverBase(IBinaryResolver binaryResolver) => BinaryResolver = binaryResolver;

    /// <summary>
    /// Initializes the driver component with the given context.
    /// </summary>
    /// <param name="context">Driver context</param>
    public virtual void Initialize(DriverContext context)
    {
      ArgumentNullException.ThrowIfNull(context);
      Context = context;
      Logger = context.LoggerFactory.CreateLogger(GetType());
    }

    /// <summary>
    /// Initializes the driver component with the given context and binary resolver.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="binaryResolver">Binary resolver</param>
    public virtual void Initialize(DriverContext context, IBinaryResolver binaryResolver)
    {
      ArgumentNullException.ThrowIfNull(context);
      ArgumentNullException.ThrowIfNull(binaryResolver);
      Context = context;
      BinaryResolver = binaryResolver;
      Logger = context.LoggerFactory.CreateLogger(GetType());
    }

    #region Global Args

    /// <summary>
    /// Builds global CLI flags from the driver context, including host (-H)
    /// and TLS certificate flags (--tlsverify/--tls, --tlscacert, --tlscert, --tlskey).
    /// </summary>
    /// <param name="context">The driver context (may be null).</param>
    /// <returns>A string of global flags to prepend to Docker commands, or empty string.</returns>
    public static string BuildGlobalArgs(DriverContext context)
    {
      if (context == null || string.IsNullOrEmpty(context.Host))
        return "";

      var sb = new StringBuilder();
      sb.Append($"-H {context.Host}");

      if (!string.IsNullOrEmpty(context.CertificatePath))
      {
        var certPath = context.CertificatePath;
        var caCert = Path.Combine(certPath, "ca.pem");
        var cert = Path.Combine(certPath, "cert.pem");
        var key = Path.Combine(certPath, "key.pem");

        if (context.VerifyTls)
          sb.Append(" --tlsverify");
        else
          sb.Append(" --tls");

        sb.Append($" --tlscacert {caCert} --tlscert {cert} --tlskey {key}");
      }

      return sb.ToString();
    }

    #endregion

    #region Command Execution

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
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, null, sudo, sudoPassword, cancellationToken).ConfigureAwait(false);
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
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, stdinData, sudo, sudoPassword, cancellationToken).ConfigureAwait(false);
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
      return await ExecuteProcessAsync(binaryPath, fullArgs, environment, null, sudo, sudoPassword, cancellationToken).ConfigureAwait(false);
    }

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
        CancellationToken cancellationToken)
    {
      // Build the actual process command based on sudo mechanism.
      // The password is NEVER placed on the command line.
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
        // Kill the child process on cancellation to prevent orphans.
        KillProcessSafely(process);
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

      // Attach does not support sudo with password (would conflict with stdin).
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
          CreateNoWindow = true,
          StandardOutputEncoding = Encoding.UTF8,
          StandardErrorEncoding = Encoding.UTF8
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
    /// <param name="context">Driver context</param>
    /// <param name="operation">Operation name</param>
    /// <param name="result">Command result</param>
    /// <returns>Error context</returns>
    protected static ErrorContext CreateErrorContext(DriverContext context, string operation, SimpleCommandResult result)
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
    /// <param name="operation">Operation name</param>
    /// <param name="result">Command result</param>
    /// <returns>Error context</returns>
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

  /// <summary>
  /// Result of a simple command execution.
  /// </summary>
  public class SimpleCommandResult
  {
    /// <summary>
    /// Whether the command succeeded (exit code 0).
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Standard output from the command.
    /// </summary>
    public string Output { get; set; }

    /// <summary>
    /// Standard error from the command.
    /// </summary>
    public string Error { get; set; }

    /// <summary>
    /// Exit code from the command.
    /// </summary>
    public int ExitCode { get; set; }
  }
}

