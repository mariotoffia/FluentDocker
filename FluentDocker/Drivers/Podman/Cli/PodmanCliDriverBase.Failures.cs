using System;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli
{
  public abstract partial class PodmanCliDriverBase
  {
    /// <summary>
    /// Resolves an error code for a failed Podman CLI command from an exception: reuses an
    /// existing <see cref="DriverException.ErrorCode"/> or falls back to classifying the
    /// exception's message via the component's own <see cref="Context"/>.
    /// </summary>
    /// <param name="ex">The exception raised while running the command.</param>
    /// <param name="fallbackCode">Code to use if the message does not indicate a known failure.</param>
    /// <returns>An error code.</returns>
    protected string FailureCode(Exception ex, string fallbackCode)
    {
      if (ex is DriverException driverException && !string.IsNullOrEmpty(driverException.ErrorCode))
        return driverException.ErrorCode;
      return FailureCode(ex?.Message, fallbackCode);
    }

    /// <summary>
    /// Resolves an error code for a failed Podman CLI command from its captured error text,
    /// using the component's own <see cref="Context"/> to decide machine-vs-remote classification.
    /// </summary>
    /// <param name="error">The captured error text.</param>
    /// <param name="fallbackCode">Code to use if the text does not indicate a connection failure.</param>
    /// <returns>An error code.</returns>
    protected string FailureCode(string error, string fallbackCode)
        => FailureCode(Context, error, fallbackCode);

    /// <summary>
    /// Resolves an error code for a failed Podman CLI command from its captured error text:
    /// <see cref="ErrorCodes.Machine.NotRunning"/> when <paramref name="context"/> is
    /// machine/VM-managed (macOS/Windows local Podman machine) and the failure looks like a
    /// daemon-connection error, <see cref="ErrorCodes.Api.ConnectionFailed"/> for a remote-host
    /// connection error, otherwise <paramref name="fallbackCode"/>.
    /// </summary>
    /// <param name="context">Driver context used to classify machine-managed vs. remote hosts.</param>
    /// <param name="error">The captured error text.</param>
    /// <param name="fallbackCode">Code to use if the text does not indicate a connection failure.</param>
    /// <returns>An error code.</returns>
    protected static string FailureCode(DriverContext context, string error, string fallbackCode)
    {
      if (!IsDaemonConnectionError(error))
        return fallbackCode;
      return IsMachineManagedContext(context) ? ErrorCodes.Machine.NotRunning : ErrorCodes.Api.ConnectionFailed;
    }

    /// <summary>
    /// Builds a <see cref="DriverException"/> for a failed command using the component's own
    /// <see cref="Context"/>, marking it transient when classified as a stopped machine/VM.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="fallbackCode">Code to use if the message does not indicate a known failure.</param>
    /// <param name="context">Error context attached to the exception.</param>
    /// <returns>A <see cref="DriverException"/> ready to throw.</returns>
    protected DriverException CreateCommandFailureException(
        string message, string fallbackCode, ErrorContext context)
        => CreateCommandFailureException(Context, message, fallbackCode, context);

    /// <summary>
    /// Builds a <see cref="DriverException"/> for a failed command, marking it transient
    /// (<see cref="DriverException.IsTransient"/>) when classified as a stopped machine/VM
    /// rather than a generic connection failure — so callers can distinguish a
    /// wait-and-retry condition from a hard connection error.
    /// </summary>
    /// <param name="driverContext">Driver context used to classify machine-managed vs. remote hosts.</param>
    /// <param name="message">Exception message.</param>
    /// <param name="fallbackCode">Code to use if the message does not indicate a known failure.</param>
    /// <param name="context">Error context attached to the exception.</param>
    /// <returns>A <see cref="DriverException"/> ready to throw.</returns>
    protected static DriverException CreateCommandFailureException(
        DriverContext driverContext, string message, string fallbackCode, ErrorContext context)
    {
      var code = FailureCode(driverContext, message, fallbackCode);
      return new DriverException(message, code, context, code == ErrorCodes.Machine.NotRunning);
    }

    /// <summary>
    /// True if <paramref name="error"/> matches one of Podman's known daemon/socket-unreachable
    /// messages (e.g. "Cannot connect to Podman", "connection refused").
    /// </summary>
    protected static bool IsDaemonConnectionError(string error)
    {
      if (string.IsNullOrEmpty(error))
        return false;
      return error.Contains("Cannot connect to Podman", StringComparison.OrdinalIgnoreCase)
          || error.Contains("error during connect", StringComparison.OrdinalIgnoreCase)
          || error.Contains("unable to connect to Podman socket", StringComparison.OrdinalIgnoreCase)
          || error.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
          || (error.Contains("dial unix", StringComparison.OrdinalIgnoreCase)
              && error.Contains("connect:", StringComparison.OrdinalIgnoreCase));
    }

    // A daemon connection error is machine-not-running whenever Podman runs behind a machine/VM.
    // That is unconditionally true on macOS/Windows (no native daemon there), independent of
    // whether AutoStartMachine was configured — so a stopped machine mid-operation is classified
    // as ErrorCodes.Machine.NotRunning (and the exception is marked transient) as the docs promise,
    // not as a generic ConnectionFailed. A remote Host, however, is never machine-managed: a
    // macOS/Windows client can still target a remote rootful daemon over ssh://, and when that
    // remote dies there is no local machine/VM to blame or restart (P-M1).
    private static bool IsMachineManagedContext(DriverContext context)
    {
      if (!string.IsNullOrEmpty(context?.Host))
        return false; // remote daemon → not a local machine/VM

      return context?.AutoStartMachine != null || PodmanCliDriverPack.MachineManagementApplies();
    }
  }
}
