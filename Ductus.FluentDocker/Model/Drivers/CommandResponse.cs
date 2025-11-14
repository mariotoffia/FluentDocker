namespace Ductus.FluentDocker.Model.Drivers
{
    /// <summary>
    /// Represents the result of a driver command execution.
    /// Replaces the old ConsoleStream<T> pattern.
    /// </summary>
    /// <typeparam name="T">The type of data returned by the command</typeparam>
    public class CommandResponse<T>
    {
        /// <summary>
        /// Indicates whether the command executed successfully.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The data returned by the command (if successful).
        /// </summary>
        public T Data { get; set; }

        /// <summary>
        /// Error message (if not successful).
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Error code for programmatic handling.
        /// </summary>
        public string ErrorCode { get; set; }

        /// <summary>
        /// Diagnostic context information.
        /// </summary>
        public ErrorContext ErrorContext { get; set; }

        /// <summary>
        /// Exit code from the command execution.
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// Standard output from the command.
        /// </summary>
        public string Output { get; set; }

        /// <summary>
        /// Creates a successful command response.
        /// </summary>
        public static CommandResponse<T> Ok(T data)
        {
            return new CommandResponse<T>
            {
                Success = true,
                Data = data,
                ExitCode = 0
            };
        }

        /// <summary>
        /// Creates a successful command response with output.
        /// </summary>
        public static CommandResponse<T> Ok(T data, string output)
        {
            return new CommandResponse<T>
            {
                Success = true,
                Data = data,
                Output = output,
                ExitCode = 0
            };
        }

        /// <summary>
        /// Creates a failed command response.
        /// </summary>
        public static CommandResponse<T> Fail(string error, string errorCode = null, int exitCode = -1)
        {
            return new CommandResponse<T>
            {
                Success = false,
                Error = error,
                ErrorCode = errorCode ?? ErrorCodes.General.Unknown,
                ExitCode = exitCode
            };
        }

        /// <summary>
        /// Creates a failed command response with error context.
        /// </summary>
        public static CommandResponse<T> Fail(string error, string errorCode, ErrorContext context, int exitCode = -1)
        {
            return new CommandResponse<T>
            {
                Success = false,
                Error = error,
                ErrorCode = errorCode,
                ErrorContext = context,
                ExitCode = exitCode
            };
        }
    }
}
