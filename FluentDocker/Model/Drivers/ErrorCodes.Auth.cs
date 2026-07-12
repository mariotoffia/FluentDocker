#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Authentication-related error codes
    /// </summary>
    public static class Auth
    {
      /// <summary>
      /// The registry login operation failed.
      /// </summary>
      public const string LoginFailed = "AUTH_001";

      /// <summary>
      /// The registry logout operation failed.
      /// </summary>
      public const string LogoutFailed = "AUTH_002";

      /// <summary>
      /// The registry rejected the supplied credentials (HTTP 401 on login).
      /// </summary>
      public const string InvalidCredentials = "AUTH_003";

      /// <summary>
      /// The specified registry server could not be found or resolved.
      /// </summary>
      public const string RegistryNotFound = "AUTH_004";
    }
  }
}
