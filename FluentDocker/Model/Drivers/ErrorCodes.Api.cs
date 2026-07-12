#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Docker API HTTP-specific error codes
    /// </summary>
    public static class Api
    {
      /// <summary>
      /// The Docker Engine API returned HTTP 400 Bad Request.
      /// </summary>
      public const string BadRequest = "API_400";

      /// <summary>
      /// The Docker Engine API returned HTTP 401 Unauthorized.
      /// </summary>
      public const string Unauthorized = "API_401";

      /// <summary>
      /// The Docker Engine API returned HTTP 403 Forbidden.
      /// </summary>
      public const string Forbidden = "API_403";

      /// <summary>
      /// The Docker Engine API returned HTTP 404 Not Found for the requested resource.
      /// </summary>
      public const string NotFound = "API_404";

      /// <summary>
      /// The Docker Engine API returned HTTP 409 Conflict.
      /// </summary>
      public const string Conflict = "API_409";

      /// <summary>
      /// The Docker Engine API returned an HTTP 5xx server error.
      /// </summary>
      public const string ServerError = "API_500";

      /// <summary>
      /// The HTTP connection to the Docker Engine API could not be established.
      /// </summary>
      public const string ConnectionFailed = "API_CONN";
      /// <summary>
      /// A boundless streaming response (e.g. <c>/events</c> without an <c>until</c> bound)
      /// ended cleanly because the daemon closed the connection — an unexpected EOF, not a
      /// transport error.
      /// </summary>
      public const string StreamEnded = "API_STREAM_ENDED";
      /// <summary>
      /// A streaming response was interrupted mid-stream by a transport read failure (for
      /// example a connection reset) after the connection had already been established.
      /// Distinct from <see cref="ConnectionFailed"/> (never connected) and
      /// <see cref="StreamEnded"/> (clean EOF) so on-call diagnosis is unambiguous.
      /// </summary>
      public const string StreamInterrupted = "API_STREAM_INTERRUPTED";
    }
  }
}
