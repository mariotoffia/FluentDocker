#nullable enable
using System;

namespace FluentDocker.Model.Drivers
{
  /// <summary>
  /// Represents the result of a driver command execution.
  /// All driver interfaces return <see cref="CommandResponse{T}"/> from their operations.
  /// Properties are publicly read-only; use the <see cref="Ok(T)"/> and <see cref="Fail(string, string, int)"/> factory methods.
  /// </summary>
  /// <typeparam name="T">The type of data returned by the command</typeparam>
#pragma warning disable CA1000 // Static members on generic type — factory pattern is intentional API design
  public sealed class CommandResponse<T>
  {
    /// <summary>
    /// Indicates whether the command executed successfully.
    /// </summary>
    public bool Success { get; private init; }

    /// <summary>
    /// The data returned by the command. Successful responses are expected to carry non-null data.
    /// </summary>
    public T? Data { get; private init; }

    /// <summary>
    /// Error message (if not successful).
    /// </summary>
    public string? Error { get; private init; }

    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    public string? ErrorCode { get; private init; }

    /// <summary>
    /// Diagnostic context information.
    /// </summary>
    public ErrorContext? ErrorContext { get; private init; }

    /// <summary>
    /// Exit code from the command execution.
    /// </summary>
    public int ExitCode { get; private init; }

    /// <summary>
    /// Standard output from the command.
    /// </summary>
    public string? Output { get; private init; }

    /// <summary>
    /// Creates a successful command response.
    /// </summary>
    /// <remarks>
    /// Successful responses are expected to carry non-null <see cref="Data"/>. A success response with
    /// null data is a broken-driver contract; the consumer combinators (<c>EnsureSuccess</c>/<c>Map</c>/
    /// <c>OnSuccess</c>) surface it as a typed <see cref="FluentDocker.Common.DriverException"/> with an
    /// error code and context rather than a context-free <see cref="NullReferenceException"/>.
    /// </remarks>
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
    /// Creates a successful command response with output and the process exit code.
    /// </summary>
    public static CommandResponse<T> Ok(T data, string output, int exitCode)
    {
      if (exitCode < 0)
        throw new ArgumentOutOfRangeException(nameof(exitCode), exitCode, "Successful command responses must not use a negative exit code.");

      return new CommandResponse<T>
      {
        Success = true,
        Data = data,
        Output = output,
        ExitCode = exitCode
      };
    }

    /// <summary>
    /// Creates a failed command response.
    /// </summary>
    public static CommandResponse<T> Fail(string error, string? errorCode = null, int exitCode = -1)
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
        ErrorCode = errorCode ?? ErrorCodes.General.Unknown,
        ErrorContext = context,
        ExitCode = exitCode
      };
    }
  }
#pragma warning restore CA1000
}
