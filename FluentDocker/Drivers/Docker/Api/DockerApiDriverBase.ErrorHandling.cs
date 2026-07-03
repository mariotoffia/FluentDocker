using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api
{
  /// <summary>
  /// Docker API driver base — error context, HTTP error mapping, and connection helpers.
  /// </summary>
  public abstract partial class DockerApiDriverBase
  {
    #region Error Context

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

    protected static string MapNotFoundErrorCode(int statusCode, string defaultErrorCode)
    {
      return statusCode == 404 ? defaultErrorCode : MapHttpErrorCode(statusCode);
    }

    protected static string MapHttpErrorCode(int statusCode)
    {
      return statusCode switch
      {
        400 => ErrorCodes.Api.BadRequest,
        401 => ErrorCodes.Api.Unauthorized,
        404 => ErrorCodes.Api.NotFound,
        409 => ErrorCodes.Api.Conflict,
        >= 500 => ErrorCodes.Api.ServerError,
        _ => ErrorCodes.Api.BadRequest
      };
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
