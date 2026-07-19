using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    protected DriverContext Context { get; private set; } = null!;

    /// <summary>
    /// Logger for this driver component. Category equals the concrete derived type's FQN.
    /// </summary>
    protected ILogger Logger { get; private set; } = NullLogger.Instance;

    /// <summary>
    /// The binary resolver for resolving Podman command paths.
    /// </summary>
    protected IPodmanBinaryResolver BinaryResolver { get; private set; } = null!;

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
    /// Podman uses --url for the remote host. Docker-style TLS is not expressible via the Podman CLI:
    /// on a <c>tcp://</c> host, setting <see cref="DriverContext.CertificatePath"/> or
    /// <see cref="DriverContext.VerifyTls"/><c>=true</c> <b>fails closed</b> with a
    /// <see cref="DriverException"/> rather than silently connecting in plaintext; on <c>ssh://</c>
    /// or <c>unix://</c> endpoints those settings are ignored with a one-time warning. Warning
    /// deduplication is process-wide and keyed by driver/host, so long-lived hosts see each once.
    /// </summary>
    /// <param name="context">The driver context (may be null).</param>
    /// <param name="logger">Optional logger used for one-time warnings about ignored settings.</param>
    /// <returns>A string of global flags to prepend to Podman commands, or empty string.</returns>
    public static string BuildGlobalArgs(DriverContext context, ILogger? logger = null)
    {
      if (context == null)
        return "";

      // Fail closed on tcp://: podman CLI cannot apply Docker-style TLS, so honoring a request for
      // it by silently connecting in plaintext with no server authentication is a security downgrade.
      // ssh:// tunnels and unix:// sockets are already secure/local, so ignoring the TLS settings there
      // is legitimate and only warrants a warning (PDM-MAJ-1).
      var wantsTls = !string.IsNullOrEmpty(context.CertificatePath) || context.VerifyTls == true;
      if (wantsTls && !string.IsNullOrEmpty(context.Host) &&
          context.Host.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
      {
        throw new DriverException(
            "Podman CLI cannot apply Docker-style TLS (CertificatePath/VerifyTls) to a tcp:// endpoint; " +
            "proceeding would connect in plaintext with no server authentication. Use an ssh:// or unix:// " +
            "endpoint, or clear CertificatePath/VerifyTls if a plaintext tcp:// connection is intended.",
            ErrorCodes.General.InvalidArgument);
      }

      if (!string.IsNullOrEmpty(context.CertificatePath))
        WarnCertificatePathIgnoredOnce(context, logger);

      if (context.VerifyTls.HasValue)
        WarnVerifyTlsIgnoredOnce(context, logger);

      if (string.IsNullOrEmpty(context.Host))
        return "";

      return $"--url {QuoteArgumentIfNeeded(context.Host)}";
    }

    private static void WarnCertificatePathIgnoredOnce(DriverContext context, ILogger? logger)
    {
      if (logger == null)
        return;

      // WarnCertificatePathIgnoredOnce is only called when CertificatePath is non-empty
      // (guarded by the caller), so the final coalesce operand is non-null.
      var key = context.DriverId ?? context.Host ?? context.CertificatePath!;
      if (CertificateWarnings.TryAdd(key, 0))
        logger.LogWarning(
            "Podman CLI ignores DriverContext.CertificatePath because podman CLI does not expose Docker-style TLS certificate flags.");
    }

    private static void WarnVerifyTlsIgnoredOnce(DriverContext context, ILogger? logger)
    {
      if (logger == null)
        return;

      var key = context.DriverId ?? context.Host ?? "default";
      if (VerifyTlsWarnings.TryAdd(key, 0))
        logger.LogWarning(
            "Podman CLI ignores DriverContext.VerifyTls because podman CLI has no Docker-style daemon TLS-verify flag; podman's --tls-verify is a per-command registry flag, not a connection setting.");
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

    /// <summary>Returns the command's captured stderr if non-empty; otherwise <paramref name="fallback"/>.</summary>
    protected static string ErrorOrDefault(SimpleCommandResult result, string fallback)
    {
      return string.IsNullOrEmpty(result?.Error) ? fallback : result.Error;
    }

    /// <summary>
    /// Concatenates buffered stdout then stderr into one string. Ordering is
    /// <b>stdout-first, then stderr</b> — the two streams are captured into separate
    /// buffers, so cross-stream chronological interleaving is <b>not</b> preserved
    /// (a crash line on stderr appears after all stdout, not where it occurred).
    /// </summary>
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
    private static (string FileName, string Arguments, string? PasswordForStdin) BuildSudoCommand(
        string binaryPath, string arguments, SudoMechanism sudo, string? sudoPassword)
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

    /// <summary>True if <paramref name="value"/> is non-empty and its first character is '-'.</summary>
    protected static bool StartsWithDash(string value) =>
        !string.IsNullOrEmpty(value) && value[0] == '-';

    /// <summary>
    /// Quotes a positional CLI argument, throwing a <see cref="DriverException"/> if it starts
    /// with '-' (which Podman would otherwise misparse as an option rather than a value).
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
            $"{argumentName} must not start with '-' because Podman would parse it as an option.",
            ErrorCodes.General.InvalidArgument);
      return QuoteArgumentIfNeeded(argument);
    }

    #endregion
  }
}
