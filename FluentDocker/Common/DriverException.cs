using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Base exception for all driver-related errors in FluentDocker v3.0.0.
  /// </summary>
  public class DriverException : FluentDockerException
  {
    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Diagnostic context information.
    /// </summary>
    public ErrorContext Context { get; }

    /// <summary>
    /// Indicates if this error is transient and may succeed on retry.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>
    /// Initializes a new instance with the specified error message and an unknown error code.
    /// </summary>
    /// <param name="message">The error message.</param>
    public DriverException(string message) : base(message) => ErrorCode = ErrorCodes.General.Unknown;

    /// <summary>
    /// Initializes a new instance with the specified error message and error code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code for programmatic handling.</param>
    public DriverException(string message, string errorCode) : base(message) => ErrorCode = errorCode;

    /// <summary>
    /// Initializes a new instance with the specified error message, error code, and transient flag.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code for programmatic handling.</param>
    /// <param name="isTransient">Whether the error is transient and may succeed on retry.</param>
    public DriverException(string message, string errorCode, bool isTransient)
        : base(message)
    {
      ErrorCode = errorCode;
      IsTransient = isTransient;
    }

    /// <summary>
    /// Initializes a new instance with the specified error message, error code, context, and optional transient flag.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code for programmatic handling.</param>
    /// <param name="context">Diagnostic context information.</param>
    /// <param name="isTransient">Whether the error is transient and may succeed on retry.</param>
    public DriverException(string message, string errorCode, ErrorContext context, bool isTransient = false)
        : base(message)
    {
      ErrorCode = errorCode;
      Context = context;
      IsTransient = isTransient;
    }

    /// <summary>
    /// Initializes a new instance with the specified error message, inner exception, and an unknown error code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public DriverException(string message, Exception innerException) : base(message, innerException) => ErrorCode = ErrorCodes.General.Unknown;

    /// <summary>
    /// Initializes a new instance with the specified error message, error code, and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code for programmatic handling.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public DriverException(string message, string errorCode, Exception innerException)
        : base(message, innerException) => ErrorCode = errorCode;

    /// <summary>
    /// Initializes a new instance with the specified error message, error code, context, inner exception, and optional transient flag.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code for programmatic handling.</param>
    /// <param name="context">Diagnostic context information.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="isTransient">Whether the error is transient and may succeed on retry.</param>
    public DriverException(string message, string errorCode, ErrorContext context, Exception innerException, bool isTransient = false)
        : base(message, innerException)
    {
      ErrorCode = errorCode;
      Context = context;
      IsTransient = isTransient;
    }

    /// <summary>Returns a string representation including error code, context, and transient status.</summary>
    public override string ToString()
    {
      var baseMessage = base.ToString();

      if (Context != null)
      {
        return $"{baseMessage}\nError Code: {ErrorCode}\nContext: {Context}\nIs Transient: {IsTransient}";
      }

      return $"{baseMessage}\nError Code: {ErrorCode}\nIs Transient: {IsTransient}";
    }
  }
}
