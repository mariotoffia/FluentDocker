using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    protected DriverContext Context { get; private set; } = null!;

    /// <summary>
    /// Logger for this driver component. Category equals the concrete derived type's FQN.
    /// Defaults to <see cref="NullLogger.Instance"/> until <see cref="Initialize(DriverContext)"/> is called.
    /// </summary>
    protected ILogger Logger { get; private set; } = NullLogger.Instance;

    /// <summary>
    /// The binary resolver for resolving Docker command paths.
    /// </summary>
    protected IBinaryResolver? BinaryResolver { get; private set; }

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

    /// <summary>Returns the command's captured stderr if non-empty; otherwise <paramref name="fallback"/>.</summary>
    protected static string ErrorOrDefault(SimpleCommandResult result, string fallback)
    {
      return string.IsNullOrEmpty(result?.Error) ? fallback : result.Error;
    }

    /// <summary>Formats a value using invariant culture, ignoring the current thread's locale.</summary>
    protected static string FormatInvariant<T>(T value)
        where T : IFormattable
        => value.ToString(null, CultureInfo.InvariantCulture);

    /// <summary>Formats a value with the given format string using invariant culture.</summary>
    protected static string FormatInvariant<T>(T value, string format)
        where T : IFormattable
        => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>
    /// Concatenates buffered stdout then stderr into one string. Ordering is
    /// <b>stdout-first, then stderr</b> — the two streams are captured into separate
    /// buffers, so cross-stream chronological interleaving is <b>not</b> preserved
    /// (a crash line on stderr appears after all stdout, not where it occurred).
    /// Callers needing arrival-ordered lines must use
    /// <see cref="IStreamDriver.StreamLogEntriesAsync"/> instead.
    /// </summary>
    protected static string MergeOutputAndError(string output, string error)
    {
      if (string.IsNullOrEmpty(output))
        return error ?? string.Empty;
      if (string.IsNullOrEmpty(error))
        return output;
      return output.EndsWith('\n') || error.StartsWith('\n') ? output + error : output + "\n" + error;
    }

    /// <summary>
    /// Resolves an error code for a failed CLI command from an exception: reuses an existing
    /// <see cref="DriverException.ErrorCode"/> or falls back to classifying the exception's message.
    /// </summary>
    /// <param name="ex">The exception raised while running the command.</param>
    /// <param name="fallbackCode">Code to use if the message does not indicate a known failure.</param>
    /// <returns>An error code.</returns>
    protected static string FailureCode(Exception ex, string fallbackCode)
    {
      if (ex is DriverException driverException && !string.IsNullOrEmpty(driverException.ErrorCode))
        return driverException.ErrorCode;
      return FailureCode(ex?.Message, fallbackCode);
    }

    /// <summary>
    /// Resolves an error code for a failed CLI command from its captured error text:
    /// <see cref="ErrorCodes.Api.ConnectionFailed"/> when the daemon is unreachable,
    /// otherwise <paramref name="fallbackCode"/>.
    /// </summary>
    /// <param name="error">The captured error text.</param>
    /// <param name="fallbackCode">Code to use if the text does not indicate a connection failure.</param>
    /// <returns>An error code.</returns>
    protected static string FailureCode(string? error, string fallbackCode)
    {
      if (IsDaemonConnectionError(error))
        return ErrorCodes.Api.ConnectionFailed;
      return fallbackCode;
    }

    /// <summary>
    /// True if <paramref name="error"/> matches one of the Docker CLI's known
    /// daemon-unreachable messages (e.g. "Cannot connect to the Docker daemon").
    /// </summary>
    protected static bool IsDaemonConnectionError(string? error)
    {
      if (string.IsNullOrEmpty(error))
        return false;
      return error.Contains("Cannot connect to the Docker daemon", StringComparison.OrdinalIgnoreCase)
          || error.Contains("error during connect", StringComparison.OrdinalIgnoreCase)
          || error.Contains("failed to connect to the docker API", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a failed <see cref="CommandResponse{T}"/> for an argument that starts with
    /// '-' and would therefore be misparsed by Docker as a CLI option rather than a value.
    /// </summary>
    protected static CommandResponse<T> FailInvalidLeadingDash<T>(string argumentName)
    {
      return CommandResponse<T>.Fail(
          $"{argumentName} must not start with '-' because Docker would parse it as an option.",
          ErrorCodes.General.InvalidArgument);
    }

    /// <summary>True if <paramref name="value"/> is non-empty and its first character is '-'.</summary>
    protected static bool StartsWithDash(string value) =>
        !string.IsNullOrEmpty(value) && value[0] == '-';

    /// <summary>
    /// Quotes a positional CLI argument, throwing a <see cref="DriverException"/> if it starts
    /// with '-' (which Docker would otherwise misparse as an option rather than a value).
    /// </summary>
    /// <param name="argument">The positional argument value (must not be null — a positional argument is required).</param>
    /// <param name="argumentName">Argument name used in the exception message.</param>
    /// <exception cref="DriverException">The argument is null or starts with '-'.</exception>
    protected static string QuotePositionalArgument(string? argument, string argumentName)
    {
      if (argument is null)
        throw new DriverException(
            $"{argumentName} is required and must not be null.",
            ErrorCodes.General.InvalidArgument);
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
    private static (string FileName, string Arguments, string? PasswordForStdin) BuildSudoCommand(
        string binaryPath, string arguments, SudoMechanism sudo, string? sudoPassword)
        => BuildSudoCommand(binaryPath, arguments, sudo, sudoPassword, null);

    /// <summary>
    /// Builds the actual process FileName and Arguments for sudo-aware execution, forwarding
    /// caller-supplied environment variable NAMES through sudo via <c>--preserve-env</c>
    /// (sudo's default <c>env_reset</c> would otherwise silently strip variables that were set
    /// on the spawned <c>sudo</c> process itself). Only names ever reach the command line —
    /// values stay in the process environment. Requires sudoers to permit <c>SETENV</c> or a
    /// matching <c>env_keep</c>; sudo fails loudly otherwise, which beats a silent drop.
    /// The password is NEVER placed on the command line — it is returned separately for stdin.
    /// </summary>
    private static (string FileName, string Arguments, string? PasswordForStdin) BuildSudoCommand(
        string binaryPath, string arguments, SudoMechanism sudo, string? sudoPassword,
        IReadOnlyCollection<string>? preserveEnvironmentNames)
    {
      var preserve = sudo != SudoMechanism.None && preserveEnvironmentNames is { Count: > 0 }
          ? $"--preserve-env={string.Join(",", preserveEnvironmentNames)} "
          : string.Empty;
      return sudo switch
      {
        SudoMechanism.NoPassword => ("sudo", $"-n {preserve}-- {QuoteArgumentIfNeeded(binaryPath)} {arguments}", null),
        SudoMechanism.Password => ("sudo", $"-S {preserve}-- {QuoteArgumentIfNeeded(binaryPath)} {arguments}", sudoPassword),
        _ => (binaryPath, arguments, null)
      };
    }

    /// <summary>
    /// Validates and returns the environment names to preserve across sudo, or <c>null</c>
    /// when no forwarding is needed (no sudo, or no extra environment). Names must be plain
    /// POSIX identifiers (<c>[A-Za-z_][A-Za-z0-9_]*</c>) so the generated
    /// <c>--preserve-env</c> list cannot be malformed or smuggle extra arguments.
    /// </summary>
    /// <exception cref="DriverException">A name is not a plain POSIX identifier.</exception>
    private static List<string>? ValidatedPreserveEnvNames(
        IDictionary<string, string>? environment, SudoMechanism sudo)
    {
      if (sudo == SudoMechanism.None || environment == null || environment.Count == 0)
        return null;

      var names = new List<string>(environment.Count);
      foreach (var name in environment.Keys)
      {
        if (!IsPosixEnvironmentName(name))
          throw new DriverException(
              $"Environment variable name '{name}' cannot be forwarded through sudo " +
              "(--preserve-env requires plain identifier names).",
              ErrorCodes.Driver.CommandExecutionFailed);
        names.Add(name);
      }

      return names;
    }

    private static bool IsPosixEnvironmentName(string name)
    {
      if (string.IsNullOrEmpty(name))
        return false;
      if (!char.IsAsciiLetter(name[0]) && name[0] != '_')
        return false;
      for (var i = 1; i < name.Length; i++)
      {
        if (!char.IsAsciiLetterOrDigit(name[i]) && name[i] != '_')
          return false;
      }

      return true;
    }

    /// <summary>
    /// Safely kills a process if it is still running, suppressing any errors.
    /// </summary>
    private static void KillProcessSafely(Process? process, ILogger? logger = null)
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
        var processFileName = process.StartInfo.FileName;
        var binarySuffix = string.Equals(processFileName, binaryPath, StringComparison.Ordinal)
            ? string.Empty
            : $" for Docker CLI binary '{binaryPath}'";
        throw new DriverException(
            $"Failed to start process '{processFileName}'{binarySuffix}.",
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
    /// Standard output from the command. Empty (never null) when the command produced no stdout.
    /// </summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// Standard error from the command. Empty (never null) when the command produced no stderr.
    /// </summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// Exit code from the command.
    /// </summary>
    public int ExitCode { get; set; }
  }
}
