using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception raised by the model-runner service layer when a driver
  /// <see cref="CommandResponse{T}"/> reports a failure, and by streaming
  /// inference methods on mid-stream faults (where a <see cref="CommandResponse{T}"/>
  /// envelope cannot represent the error). Carries the originating
  /// <see cref="DriverException.ErrorCode"/> and <see cref="DriverException.Context"/>.
  /// </summary>
  public class ModelRunnerException : DriverException
  {
    /// <summary>
    /// Initializes a new instance with a message, optional error code and optional inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The model error code (defaults to <see cref="ErrorCodes.General.Unknown"/>).</param>
    /// <param name="inner">The exception that caused this error, if any.</param>
    public ModelRunnerException(string message, string errorCode = null, Exception inner = null)
        : base(message, errorCode ?? ErrorCodes.General.Unknown, inner)
    {
    }

    /// <summary>
    /// Initializes a new instance with a message, error code, diagnostic context and optional inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The model error code.</param>
    /// <param name="context">Diagnostic context (driver, operation, exit code, stderr).</param>
    /// <param name="inner">The exception that caused this error, if any.</param>
    public ModelRunnerException(string message, string errorCode, ErrorContext context, Exception inner = null)
        : base(message, errorCode ?? ErrorCodes.General.Unknown, context, inner)
    {
    }
  }
}
