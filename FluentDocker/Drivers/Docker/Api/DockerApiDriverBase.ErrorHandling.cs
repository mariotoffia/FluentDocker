using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api
{
  /// <summary>
  /// Docker API driver base — error context, HTTP error mapping, and connection helpers.
  /// </summary>
  public abstract partial class DockerApiDriverBase
  {
    #region Error Context

    /// <inheritdoc />
    protected ErrorContext CreateErrorContext(
        string operation, int statusCode, string responseBody = null)
    {
      return new ErrorContext(operation)
      {
        DriverId = Context?.DriverId,
        Host = Context?.Host,
        ExitCode = statusCode,
        StdOut = responseBody,
        Metadata = { ["HttpStatusCode"] = statusCode.ToString(CultureInfo.InvariantCulture) }
      };
    }

    /// <summary>
    /// Maps 404 to an operation-specific code and all other statuses to generic API codes.
    /// </summary>
    /// <remarks>
    /// Docker API 404 handling intentionally varies by verb/caller: resource lookups usually
    /// expose NotFound, while operation endpoints can return their operation-failed code.
    /// </remarks>
    protected static string MapNotFoundErrorCode(int statusCode, string defaultErrorCode)
    {
      return statusCode == 404 ? defaultErrorCode : MapHttpErrorCode(statusCode);
    }

    /// <inheritdoc />
    protected static string MapHttpErrorCode(int statusCode)
    {
      return statusCode switch
      {
        408 => ErrorCodes.General.Timeout,
        599 => ErrorCodes.Api.ConnectionFailed,
        400 => ErrorCodes.Api.BadRequest,
        401 => ErrorCodes.Api.Unauthorized,
        403 => ErrorCodes.Api.Unauthorized,
        404 => ErrorCodes.Api.NotFound,
        409 => ErrorCodes.Api.Conflict,
        >= 500 => ErrorCodes.Api.ServerError,
        _ => ErrorCodes.Api.BadRequest
      };
    }

    /// <inheritdoc />
    protected ApiResult<T> TransportFailure<T>(Exception ex)
    {
      var (statusCode, message) = DescribeTransportFailure(ex);
      return ApiResult<T>.Failure(statusCode, message);
    }

    /// <inheritdoc />
    protected ApiResult TransportFailure(Exception ex)
    {
      var (statusCode, message) = DescribeTransportFailure(ex);
      return ApiResult.Failure(statusCode, message);
    }

    // Classifies a pre-response transport exception into a synthetic HTTP status + message.
    // 408 (an internal HttpClient.Timeout — the caller's token did not fire) maps to
    // General.Timeout; 599 (daemon never reached) maps to Api.ConnectionFailed — both via
    // MapHttpErrorCode — so a daemon-down outage is distinguishable from a genuine daemon 5xx.
    /// <inheritdoc />
    protected (int StatusCode, string Message) DescribeTransportFailure(Exception ex)
    {
      if (ex is DockerApiTtfbTimeoutException ttfb)
        return (408, $"Docker API connection/TTFB timed out after {ttfb.Timeout}: {ttfb.InnerException?.Message ?? ttfb.Message}");

      if (ex is TaskCanceledException)
      {
        var timeout = Context?.RequestTimeout ?? TimeSpan.FromMinutes(5);
        return (408, $"Docker API request timed out after {timeout}: {ex.Message}");
      }

      return (599, $"Cannot connect to Docker daemon: {ex.Message}");
    }

    // Classifies a streaming open/read exception into an error code. A real HTTP status
    // (e.g. 404 from EnsureStreamSuccessAsync) maps directly; a pre-response transport
    // failure is described (599 connect / 408 timeout) so daemon-down streams surface as
    // Api.ConnectionFailed uniformly with the buffered paths.
    /// <inheritdoc />
    protected string ClassifyStreamException(Exception ex)
    {
      if (ex is HttpRequestException { StatusCode: not null } http)
        return MapHttpErrorCode((int)http.StatusCode.Value);

      var (statusCode, _) = DescribeTransportFailure(ex);
      return MapHttpErrorCode(statusCode);
    }

    #endregion

    // Caller-initiated cancellation (an OperationCanceledException whose token is the
    // caller's) is NOT a connection failure — returning false here lets the OCE escape the
    // `when` filter and propagate, instead of being masked as a 503 "cannot connect".
    // An internal HttpClient.Timeout surfaces as a TaskCanceledException whose token is NOT
    // the caller's, so ct.IsCancellationRequested is false and it is still treated as a
    // connection error below.
    private static bool IsConnectionError(Exception ex, CancellationToken ct)
    {
      if (ex is OperationCanceledException && ct.IsCancellationRequested)
        return false;

      return ex is HttpRequestException or System.Net.Sockets.SocketException or TaskCanceledException;
    }
  }
}
