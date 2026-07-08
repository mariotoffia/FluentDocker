#nullable enable
using System;
using System.Collections.Generic;

namespace FluentDocker.Model.Drivers
{
  /// <summary>
  /// Provides diagnostic context information for errors that occur during driver operations.
  /// </summary>
  public class ErrorContext
  {
    private string? _operationId;
    private string? _driverId;
    private string? _host;
    private string? _operation;
    private int? _exitCode;
    private string? _stdOut;
    private string? _stdErr;
    private Dictionary<string, string> _metadata = [];
    private DateTime _timestamp = DateTime.UtcNow;

    /// <summary>
    /// Unique identifier for this operation (for tracing and correlation).
    /// </summary>
    public string? OperationId { get => _operationId; init => _operationId = value; }

    /// <summary>
    /// The driver ID where the error occurred.
    /// </summary>
    public string? DriverId { get => _driverId; init => _driverId = value; }

    /// <summary>
    /// The host where the error occurred.
    /// </summary>
    public string? Host { get => _host; init => _host = value; }

    /// <summary>
    /// The operation that was being performed.
    /// </summary>
    public string? Operation { get => _operation; init => _operation = value; }

    /// <summary>
    /// Exit code from the underlying command (if applicable).
    /// </summary>
    public int? ExitCode { get => _exitCode; init => _exitCode = value; }

    /// <summary>
    /// Standard output from the underlying command.
    /// </summary>
    public string? StdOut { get => _stdOut; init => _stdOut = value; }

    /// <summary>
    /// Standard error from the underlying command.
    /// </summary>
    public string? StdErr { get => _stdErr; init => _stdErr = value; }

    /// <summary>
    /// Additional metadata about the error.
    /// </summary>
    public Dictionary<string, string> Metadata { get => _metadata; init => _metadata = value ?? []; }

    /// <summary>
    /// Timestamp when the error occurred.
    /// </summary>
    public DateTime Timestamp { get => _timestamp; init => _timestamp = value; }

    /// <summary>
    /// Creates a new error context.
    /// </summary>
    public ErrorContext()
    {
    }

    /// <summary>
    /// Creates a new error context with the specified operation.
    /// </summary>
    public ErrorContext(string operation) => Operation = operation;

    internal void SetOperationId(string? value) => _operationId = value;
    internal void SetDriverId(string? value) => _driverId = value;
    internal void SetHost(string? value) => _host = value;
    internal void SetOperation(string? value) => _operation = value;
    internal void SetExitCode(int? value) => _exitCode = value;
    internal void SetStdOut(string? value) => _stdOut = value;
    internal void SetStdErr(string? value) => _stdErr = value;

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
