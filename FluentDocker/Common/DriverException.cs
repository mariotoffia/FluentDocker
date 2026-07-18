#nullable enable
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
    public ErrorContext? Context { get; }

    /// <summary>
    /// Indicates if this error is transient and may succeed on retry.
    /// One rule applies on every constructor: the flag is
    /// <c>isTransient || ErrorCodes.IsTransientCode(errorCode)</c> — a caller can force an
    /// error to be treated as transient, but cannot mask an error code the library
    /// classifies as transient (optional-parameter defaults cannot distinguish "explicitly
    /// false" from "unspecified"). Retry policies therefore behave identically whether or
    /// not diagnostic context was attached. Derived exception types with a GENUINELY
    /// explicit transiency contract (e.g. a permanent configuration error that reuses a
    /// transient error code) may deliberately override the classification from their own
    /// constructor via the protected setter.
    /// </summary>
    public bool IsTransient { get; protected set; }

    /// <summary>
    /// Initializes a new instance with the specified error message and an unknown error code.
    /// </summary>
    /// <param name="message">The error message.</param>
    public DriverException(string message) : base(message)
    {
      ErrorCode = ErrorCodes.General.Unknown;
      IsTransient = ErrorCodes.IsTransientCode(ErrorCode);
    }

    /// <summary>
    /// Initializes a new instance with the specified error message and error code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code for programmatic handling.</param>
    public DriverException(string message, string errorCode) : base(message)
    {
      ErrorCode = errorCode;
      IsTransient = ErrorCodes.IsTransientCode(errorCode);
    }

    /// <summary>
    /// Initializes a new instance with the specified error message, error code, and transient flag.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code for programmatic handling.</param>
    /// <param name="isTransient">Whether the error is transient and may succeed on retry (combined with the code classification — see <see cref="IsTransient"/>).</param>
    public DriverException(string message, string errorCode, bool isTransient)
        : base(message)
    {
      ErrorCode = errorCode;
      IsTransient = isTransient || ErrorCodes.IsTransientCode(errorCode);
    }

    /// <summary>
    /// Initializes a new instance with the specified error message, error code, context, and optional transient flag.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code for programmatic handling.</param>
    /// <param name="context">Diagnostic context information.</param>
    /// <param name="isTransient">Whether the error is transient and may succeed on retry.</param>
    public DriverException(string message, string errorCode, ErrorContext? context, bool isTransient = false)
        : base(message)
    {
      ErrorCode = errorCode;
      Context = context;
      IsTransient = isTransient || ErrorCodes.IsTransientCode(errorCode);
    }

    /// <summary>
    /// Initializes a new instance with the specified error message, inner exception, and an unknown error code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public DriverException(string message, Exception? innerException) : base(message, innerException)
    {
      ErrorCode = ErrorCodes.General.Unknown;
      IsTransient = ErrorCodes.IsTransientCode(ErrorCode);
    }

    /// <summary>
    /// Initializes a new instance with the specified error message, error code, and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code for programmatic handling.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public DriverException(string message, string errorCode, Exception? innerException)
        : base(message, innerException)
    {
      ErrorCode = errorCode;
      IsTransient = ErrorCodes.IsTransientCode(errorCode);
    }

    /// <summary>
    /// Initializes a new instance with the specified error message, error code, context, inner exception, and optional transient flag.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code for programmatic handling.</param>
    /// <param name="context">Diagnostic context information.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    /// <param name="isTransient">Whether the error is transient and may succeed on retry.</param>
    public DriverException(string message, string errorCode, ErrorContext? context, Exception? innerException, bool isTransient = false)
        : base(message, innerException)
    {
      ErrorCode = errorCode;
      Context = context;
      IsTransient = isTransient || ErrorCodes.IsTransientCode(errorCode);
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
