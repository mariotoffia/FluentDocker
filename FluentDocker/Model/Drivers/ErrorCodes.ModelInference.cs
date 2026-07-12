#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Model inference (OpenAI-compatible data plane) error codes.
    /// </summary>
    public static class ModelInference
    {
      /// <summary>
      /// A model inference request failed for a reason other than timeout, transport, or a
      /// specifically classified HTTP status; the default/fallback inference failure code.
      /// </summary>
      public const string RequestFailed = "MIN_001";

      /// <summary>
      /// A streamed inference response (a Server-Sent Events chunk) could not be parsed.
      /// </summary>
      public const string StreamParseError = "MIN_002";

      /// <summary>
      /// The model runner's inference endpoint could not be reached (connection failure).
      /// </summary>
      public const string EndpointUnreachable = "MIN_003";

      /// <summary>
      /// The inference request targeted a model that is not currently loaded into the runner.
      /// </summary>
      public const string ModelNotLoaded = "MIN_004";

      /// <summary>
      /// The requested inference operation is not supported by this connection/driver.
      /// </summary>
      public const string NotSupported = "MIN_005";

      /// <summary>
      /// A per-request or streaming idle timeout elapsed (the connection's
      /// <c>SendWithTimeoutAsync</c> / stream-read idle window fired). Distinct from a caller
      /// cancellation (surfaced as <see cref="System.OperationCanceledException"/>) and from a generic
      /// server <see cref="RequestFailed"/>, so callers can retry/backoff on latency specifically.
      /// </summary>
      public const string Timeout = "MIN_006";

      /// <summary>
      /// The server returned a transient overload/unavailable response (HTTP 503 Service
      /// Unavailable or 429 Too Many Requests), such as model cold-loading or rate limit;
      /// retry with backoff.
      /// </summary>
      public const string ServiceUnavailable = "MIN_007";

      /// <summary>
      /// The model runner (or its owned connection) was disposed while a stream was being
      /// enumerated; terminal — not retriable.
      /// </summary>
      public const string Disposed = "MIN_008";

      /// <summary>
      /// The inference endpoint rejected the request as unauthorized (HTTP 401), e.g. a
      /// missing or invalid bearer token.
      /// </summary>
      public const string Unauthorized = "MIN_401";
    }
  }
}
