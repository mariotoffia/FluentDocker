using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using FluentDocker.Common;
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
  public abstract partial class DockerCliDriverBase
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
      if (context == null)
        return "";

      // Host and cert paths flow into the single-string ProcessStartInfo.Arguments and
      // are parsed into argv by the runtime, so any spaces/metacharacters in them (a
      // host string or a cert directory path containing a space) must be quoted to
      // stay within a single argv token.
      var sb = new StringBuilder();
      if (!string.IsNullOrEmpty(context.Host))
        sb.Append("-H ").Append(QuoteArgumentIfNeeded(context.Host));

      if (!string.IsNullOrEmpty(context.CertificatePath))
      {
        var certPath = context.CertificatePath;
        var caCert = Path.Combine(certPath, "ca.pem");
        var cert = Path.Combine(certPath, "cert.pem");
        var key = Path.Combine(certPath, "key.pem");

        if (context.VerifyTls != false)
          AppendWithSpace(sb, "--tlsverify");
        else
          AppendWithSpace(sb, "--tls");

        AppendWithSpace(sb, "--tlscacert ").Append(QuoteArgumentIfNeeded(caCert))
          .Append(" --tlscert ").Append(QuoteArgumentIfNeeded(cert))
          .Append(" --tlskey ").Append(QuoteArgumentIfNeeded(key));
      }

      return sb.ToString();
    }

    private static StringBuilder AppendWithSpace(StringBuilder sb, string value)
    {
      if (sb.Length > 0)
        sb.Append(' ');
      return sb.Append(value);
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
        DriverId = context?.DriverId,
        Host = context?.Host,
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

    protected static string FailureCode(Exception ex, string fallbackCode)
    {
      if (ex is DriverException driverException && !string.IsNullOrEmpty(driverException.ErrorCode))
        return driverException.ErrorCode;
      return FailureCode(ex?.Message, fallbackCode);
    }

    protected static string FailureCode(string error, string fallbackCode)
    {
      if (IsDaemonConnectionError(error))
        return ErrorCodes.Api.ConnectionFailed;
      return fallbackCode;
    }

    protected static bool IsDaemonConnectionError(string error)
    {
      if (string.IsNullOrEmpty(error))
        return false;
      return error.Contains("Cannot connect to the Docker daemon", StringComparison.OrdinalIgnoreCase)
          || error.Contains("error during connect", StringComparison.OrdinalIgnoreCase)
          || error.Contains("failed to connect to the docker API", StringComparison.OrdinalIgnoreCase);
    }

    protected static CommandResponse<T> FailInvalidLeadingDash<T>(string argumentName)
    {
      return CommandResponse<T>.Fail(
          $"{argumentName} must not start with '-' because Docker would parse it as an option.",
          ErrorCodes.General.InvalidArgument);
    }

    protected static bool StartsWithDash(string value) =>
        !string.IsNullOrEmpty(value) && value[0] == '-';

    protected static string QuotePositionalArgument(string argument, string argumentName)
    {
      if (StartsWithDash(argument))
        throw new DriverException(
            $"{argumentName} must not start with '-' because Docker would parse it as an option.",
            ErrorCodes.General.InvalidArgument);
      return QuoteArgumentIfNeeded(argument);
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

    private static void StartProcessOrThrow(Process process, string binaryPath)
    {
      try
      {
        process.Start();
      }
      catch (Exception ex)
      {
        throw new DriverException(
            $"Failed to start Docker CLI binary '{binaryPath}'.",
            ErrorCodes.Driver.CommandExecutionFailed,
            ex);
      }
    }

    #endregion

    #region Argument Quoting

    /// <summary>
    /// Quotes a command-line argument if it contains shell metacharacters or whitespace,
    /// using the CommandLineToArgvW algorithm so Windows paths with backslashes are not
    /// corrupted. Interior backslashes are only doubled when they precede a literal
    /// double-quote or appear at the end of the (quoted) argument.
    /// </summary>
    protected static string QuoteArgumentIfNeeded(string argument)
    {
      return CommandLineQuoting.QuoteArgumentIfNeeded(argument);
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
