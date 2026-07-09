using System;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli
{
  public abstract partial class PodmanCliDriverBase
  {
    protected string FailureCode(Exception ex, string fallbackCode)
    {
      if (ex is DriverException driverException && !string.IsNullOrEmpty(driverException.ErrorCode))
        return driverException.ErrorCode;
      return FailureCode(ex?.Message, fallbackCode);
    }

    protected string FailureCode(string error, string fallbackCode)
        => FailureCode(Context, error, fallbackCode);

    protected string FailureCode(DriverContext context, string error, string fallbackCode)
    {
      if (!IsDaemonConnectionError(error))
        return fallbackCode;
      return IsMachineManagedContext(context) ? ErrorCodes.Machine.NotRunning : ErrorCodes.Api.ConnectionFailed;
    }

    protected DriverException CreateCommandFailureException(
        string message, string fallbackCode, ErrorContext context)
        => CreateCommandFailureException(Context, message, fallbackCode, context);

    protected DriverException CreateCommandFailureException(
        DriverContext driverContext, string message, string fallbackCode, ErrorContext context)
    {
      var code = FailureCode(driverContext, message, fallbackCode);
      return new DriverException(message, code, context, code == ErrorCodes.Machine.NotRunning);
    }

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

    private static bool IsMachineManagedContext(DriverContext context)
        => context?.AutoStartMachine != null;
  }
}
