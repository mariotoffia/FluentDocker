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

        public DriverException(string message) : base(message)
        {
            ErrorCode = ErrorCodes.General.Unknown;
        }

        public DriverException(string message, string errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        public DriverException(string message, string errorCode, bool isTransient)
            : base(message)
        {
            ErrorCode = errorCode;
            IsTransient = isTransient;
        }

        public DriverException(string message, string errorCode, ErrorContext context, bool isTransient = false)
            : base(message)
        {
            ErrorCode = errorCode;
            Context = context;
            IsTransient = isTransient;
        }

        public DriverException(string message, Exception innerException) : base(message, innerException)
        {
            ErrorCode = ErrorCodes.General.Unknown;
        }

        public DriverException(string message, string errorCode, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        public DriverException(string message, string errorCode, ErrorContext context, Exception innerException, bool isTransient = false)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            Context = context;
            IsTransient = isTransient;
        }

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
