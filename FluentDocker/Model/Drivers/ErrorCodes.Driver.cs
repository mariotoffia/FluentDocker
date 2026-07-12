#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Driver-related error codes
    /// </summary>
    public static class Driver
    {
      /// <summary>
      /// No driver is registered under the given ID.
      /// </summary>
      public const string NotFound = "DRV_001";

      /// <summary>
      /// A driver (or driver pack) is already registered under the given ID.
      /// </summary>
      public const string AlreadyRegistered = "DRV_002";

      /// <summary>
      /// The driver's underlying runtime (CLI binary or daemon) could not be reached or is
      /// not usable.
      /// </summary>
      public const string NotAvailable = "DRV_003";

      /// <summary>
      /// The driver (or driver pack) failed to initialize.
      /// </summary>
      public const string InitializationFailed = "DRV_004";

      /// <summary>
      /// The driver's health/readiness check failed.
      /// </summary>
      public const string HealthCheckFailed = "DRV_005";

      /// <summary>
      /// The driver does not implement the requested port interface
      /// (see <see cref="FluentDocker.Common.InterfaceNotSupportedException"/>).
      /// </summary>
      public const string InterfaceNotSupported = "DRV_006";

      /// <summary>
      /// The driver does not support the requested capability
      /// (see <see cref="FluentDocker.Common.CapabilityNotSupportedException"/>).
      /// </summary>
      public const string CapabilityNotSupported = "DRV_007";

      /// <summary>
      /// The underlying CLI process failed to start, or a running command could not be
      /// executed to completion.
      /// </summary>
      public const string CommandExecutionFailed = "DRV_008";
    }
  }
}
