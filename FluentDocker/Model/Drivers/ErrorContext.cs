using System.Collections.Generic;

namespace FluentDocker.Model.Drivers
{
    /// <summary>
    /// Provides diagnostic context information for errors that occur during driver operations.
    /// </summary>
    public class ErrorContext
    {
        /// <summary>
        /// Unique identifier for this operation (for tracing and correlation).
        /// </summary>
        public string OperationId { get; set; }

        /// <summary>
        /// The driver ID where the error occurred.
        /// </summary>
        public string DriverId { get; set; }

        /// <summary>
        /// The host where the error occurred.
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// The operation that was being performed.
        /// </summary>
        public string Operation { get; set; }

        /// <summary>
        /// Exit code from the underlying command (if applicable).
        /// </summary>
        public int? ExitCode { get; set; }

        /// <summary>
        /// Standard output from the underlying command.
        /// </summary>
        public string StdOut { get; set; }

        /// <summary>
        /// Standard error from the underlying command.
        /// </summary>
        public string StdErr { get; set; }

        /// <summary>
        /// Additional metadata about the error.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Timestamp when the error occurred.
        /// </summary>
        public System.DateTime Timestamp { get; set; } = System.DateTime.UtcNow;

        /// <summary>
        /// Creates a new error context.
        /// </summary>
        public ErrorContext()
        {
        }

        /// <summary>
        /// Creates a new error context with the specified operation.
        /// </summary>
        public ErrorContext(string operation)
        {
            Operation = operation;
        }

        /// <summary>
        /// Returns a string representation of the error context.
        /// </summary>
        public override string ToString()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(Operation))
                parts.Add($"Operation: {Operation}");

            if (!string.IsNullOrEmpty(DriverId))
                parts.Add($"Driver: {DriverId}");

            if (!string.IsNullOrEmpty(Host))
                parts.Add($"Host: {Host}");

            if (ExitCode.HasValue)
                parts.Add($"ExitCode: {ExitCode.Value}");

            if (!string.IsNullOrEmpty(OperationId))
                parts.Add($"OperationId: {OperationId}");

            return string.Join(", ", parts);
        }
    }
}
