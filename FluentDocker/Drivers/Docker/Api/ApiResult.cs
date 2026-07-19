namespace FluentDocker.Drivers.Docker.Api
{
#pragma warning disable CA1000 // Static members on generic type — factory pattern is intentional API design
  /// <summary>
  /// Result of a Docker API request that returns a typed payload.
  /// </summary>
  /// <typeparam name="T">The response payload type.</typeparam>
  public class ApiResult<T>
  {
    /// <summary>Gets whether the HTTP request completed successfully.</summary>
    public bool Success { get; private init; }

    /// <summary>Gets the deserialized response payload for successful requests.</summary>
    public T Data { get; private init; } = default!;

    /// <summary>Gets the HTTP status code returned by Docker, or a synthetic status for transport failures.</summary>
    public int StatusCode { get; private init; }

    /// <summary>Gets the Docker or transport error message for failed requests.</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>Gets the raw response body captured for failed requests, when available.</summary>
    public string? ResponseBody { get; private init; }

    /// <summary>Creates a successful API result.</summary>
    public static ApiResult<T> Ok(T data, int statusCode = 200) =>
        new() { Success = true, Data = data, StatusCode = statusCode };

    /// <summary>Creates a failed API result.</summary>
    public static ApiResult<T> Failure(int statusCode, string error, string? body = null) =>
        new()
        {
          Success = false,
          StatusCode = statusCode,
          ErrorMessage = error,
          ResponseBody = body
        };
  }
#pragma warning restore CA1000

  /// <summary>
  /// Result of a Docker API request that does not return a typed payload.
  /// </summary>
  public class ApiResult
  {
    /// <summary>Gets whether the HTTP request completed successfully.</summary>
    public bool Success { get; private init; }

    /// <summary>Gets the HTTP status code returned by Docker, or a synthetic status for transport failures.</summary>
    public int StatusCode { get; private init; }

    /// <summary>Gets the Docker or transport error message for failed requests.</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>Gets the raw response body captured for failed requests, when available.</summary>
    public string? ResponseBody { get; private init; }

    /// <summary>Creates a successful API result.</summary>
    public static ApiResult Ok(int statusCode = 200) =>
        new() { Success = true, StatusCode = statusCode };

    /// <summary>Creates a failed API result.</summary>
    public static ApiResult Failure(int statusCode, string error, string? body = null) =>
        new()
        {
          Success = false,
          StatusCode = statusCode,
          ErrorMessage = error,
          ResponseBody = body
        };
  }
}
