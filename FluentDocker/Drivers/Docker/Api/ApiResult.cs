namespace FluentDocker.Drivers.Docker.Api
{
#pragma warning disable CA1000 // Static members on generic type — factory pattern is intentional API design
  public class ApiResult<T>
  {
    public bool Success { get; private init; }
    public T Data { get; private init; }
    public int StatusCode { get; private init; }
    public string ErrorMessage { get; private init; }
    public string ResponseBody { get; private init; }

    public static ApiResult<T> Ok(T data) =>
        new() { Success = true, Data = data, StatusCode = 200 };

    public static ApiResult<T> Failure(int statusCode, string error, string body = null) =>
        new() { Success = false, StatusCode = statusCode, ErrorMessage = error, ResponseBody = body };
  }
#pragma warning restore CA1000

  public class ApiResult
  {
    public bool Success { get; private init; }
    public int StatusCode { get; private init; }
    public string ErrorMessage { get; private init; }
    public string ResponseBody { get; private init; }

    public static ApiResult Ok() =>
        new() { Success = true, StatusCode = 200 };

    public static ApiResult Failure(int statusCode, string error, string body = null) =>
        new() { Success = false, StatusCode = statusCode, ErrorMessage = error, ResponseBody = body };
  }
}
