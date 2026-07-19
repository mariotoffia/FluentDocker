#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Configuration error codes
    /// </summary>
    public static class Config
    {
      /// <summary>
      /// A supplied configuration value is invalid or malformed.
      /// </summary>
      public const string Invalid = "CFG_001";

      /// <summary>
      /// A required configuration value was not supplied (e.g. a missing build context path).
      /// </summary>
      public const string Missing = "CFG_002";

      /// <summary>
      /// Validation of a configuration object failed.
      /// </summary>
      public const string ValidationFailed = "CFG_003";
    }
  }
}
