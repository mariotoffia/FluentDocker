#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// General error codes
    /// </summary>
    public static class General
    {
      /// <summary>
      /// The default/fallback error code assigned to a failure that has no more specific
      /// classification (e.g. <see cref="CommandResponse{T}.Fail(string, string?, int, string?)"/>
      /// when no <c>errorCode</c> is supplied).
      /// </summary>
      public const string Unknown = "GEN_000";

      /// <summary>
      /// A command reported success but returned no data payload, violating the success/data
      /// contract (see <see cref="CommandResponse{T}.Ok(T)"/>).
      /// </summary>
      public const string InvalidOperation = "GEN_001";

      /// <summary>
      /// A caller-supplied argument was missing, malformed, or otherwise invalid for the
      /// requested operation.
      /// </summary>
      public const string InvalidArgument = "GEN_002";

      /// <summary>
      /// The operation did not complete within its configured timeout.
      /// </summary>
      public const string Timeout = "GEN_003";

      /// <summary>
      /// The operation was cancelled before it completed (e.g. via a caller-supplied
      /// cancellation token).
      /// </summary>
      public const string Cancelled = "GEN_004";
    }
  }
}
