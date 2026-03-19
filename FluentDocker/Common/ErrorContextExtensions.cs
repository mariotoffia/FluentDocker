using System;
using System.Collections.Generic;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Extension methods for ErrorContext to facilitate error context enrichment.
  /// </summary>
  public static class ErrorContextExtensions
  {
    /// <summary>
    /// Adds a metadata entry to the error context.
    /// </summary>
    /// <param name="context">The error context.</param>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The metadata value.</param>
    /// <returns>The same error context for fluent chaining.</returns>
    public static ErrorContext WithMetadata(this ErrorContext context, string key, string value)
    {
      ArgumentNullException.ThrowIfNull(context);
      context.Metadata ??= new Dictionary<string, string>();
      context.Metadata[key] = value;
      return context;
    }

    /// <summary>
    /// Sets the driver ID on the error context.
    /// </summary>
    /// <param name="context">The error context.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <returns>The same error context for fluent chaining.</returns>
    public static ErrorContext WithDriverId(this ErrorContext context, string driverId)
    {
      ArgumentNullException.ThrowIfNull(context);
      context.DriverId = driverId;
      return context;
    }

    /// <summary>
    /// Sets the host on the error context.
    /// </summary>
    /// <param name="context">The error context.</param>
    /// <param name="host">The host.</param>
    /// <returns>The same error context for fluent chaining.</returns>
    public static ErrorContext WithHost(this ErrorContext context, string host)
    {
      ArgumentNullException.ThrowIfNull(context);
      context.Host = host;
      return context;
    }

    /// <summary>
    /// Sets the exit code on the error context.
    /// </summary>
    /// <param name="context">The error context.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <returns>The same error context for fluent chaining.</returns>
    public static ErrorContext WithExitCode(this ErrorContext context, int exitCode)
    {
      ArgumentNullException.ThrowIfNull(context);
      context.ExitCode = exitCode;
      return context;
    }

    /// <summary>
    /// Sets the standard output on the error context.
    /// </summary>
    /// <param name="context">The error context.</param>
    /// <param name="stdOut">The standard output.</param>
    /// <returns>The same error context for fluent chaining.</returns>
    public static ErrorContext WithStdOut(this ErrorContext context, string stdOut)
    {
      ArgumentNullException.ThrowIfNull(context);
      context.StdOut = stdOut;
      return context;
    }

    /// <summary>
    /// Sets the standard error on the error context.
    /// </summary>
    /// <param name="context">The error context.</param>
    /// <param name="stdErr">The standard error.</param>
    /// <returns>The same error context for fluent chaining.</returns>
    public static ErrorContext WithStdErr(this ErrorContext context, string stdErr)
    {
      ArgumentNullException.ThrowIfNull(context);
      context.StdErr = stdErr;
      return context;
    }

    /// <summary>
    /// Sets the operation ID for tracing on the error context.
    /// </summary>
    /// <param name="context">The error context.</param>
    /// <param name="operationId">The operation ID.</param>
    /// <returns>The same error context for fluent chaining.</returns>
    public static ErrorContext WithOperationId(this ErrorContext context, string operationId)
    {
      ArgumentNullException.ThrowIfNull(context);
      context.OperationId = operationId;
      return context;
    }

    /// <summary>
    /// Sets the operation name on the error context.
    /// </summary>
    /// <param name="context">The error context.</param>
    /// <param name="operation">The operation name.</param>
    /// <returns>The same error context for fluent chaining.</returns>
    public static ErrorContext WithOperation(this ErrorContext context, string operation)
    {
      ArgumentNullException.ThrowIfNull(context);
      context.Operation = operation;
      return context;
    }

    /// <summary>
    /// Creates an error context for a container operation.
    /// </summary>
    /// <param name="operation">The operation being performed.</param>
    /// <param name="containerId">The container ID.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <returns>A new error context.</returns>
    public static ErrorContext ForContainer(string operation, string containerId, string driverId = null)
    {
      var context = new ErrorContext(operation);
      context.Metadata["containerId"] = containerId;
      if (!string.IsNullOrEmpty(driverId))
        context.DriverId = driverId;
      return context;
    }

    /// <summary>
    /// Creates an error context for a network operation.
    /// </summary>
    /// <param name="operation">The operation being performed.</param>
    /// <param name="networkId">The network ID.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <returns>A new error context.</returns>
    public static ErrorContext ForNetwork(string operation, string networkId, string driverId = null)
    {
      var context = new ErrorContext(operation);
      context.Metadata["networkId"] = networkId;
      if (!string.IsNullOrEmpty(driverId))
        context.DriverId = driverId;
      return context;
    }

    /// <summary>
    /// Creates an error context for a volume operation.
    /// </summary>
    /// <param name="operation">The operation being performed.</param>
    /// <param name="volumeName">The volume name.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <returns>A new error context.</returns>
    public static ErrorContext ForVolume(string operation, string volumeName, string driverId = null)
    {
      var context = new ErrorContext(operation);
      context.Metadata["volumeName"] = volumeName;
      if (!string.IsNullOrEmpty(driverId))
        context.DriverId = driverId;
      return context;
    }

    /// <summary>
    /// Creates an error context for an image operation.
    /// </summary>
    /// <param name="operation">The operation being performed.</param>
    /// <param name="imageName">The image name.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <returns>A new error context.</returns>
    public static ErrorContext ForImage(string operation, string imageName, string driverId = null)
    {
      var context = new ErrorContext(operation);
      context.Metadata["imageName"] = imageName;
      if (!string.IsNullOrEmpty(driverId))
        context.DriverId = driverId;
      return context;
    }

    /// <summary>
    /// Creates an error context for a compose operation.
    /// </summary>
    /// <param name="operation">The operation being performed.</param>
    /// <param name="projectName">The compose project name.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <returns>A new error context.</returns>
    public static ErrorContext ForCompose(string operation, string projectName, string driverId = null)
    {
      var context = new ErrorContext(operation);
      context.Metadata["projectName"] = projectName;
      if (!string.IsNullOrEmpty(driverId))
        context.DriverId = driverId;
      return context;
    }
  }

  /// <summary>
  /// Extension methods for CommandResponse to facilitate error handling.
  /// </summary>
  public static class CommandResponseExtensions
  {
    /// <summary>
    /// Ensures the command response was successful, otherwise throws an exception.
    /// </summary>
    /// <typeparam name="T">The response data type.</typeparam>
    /// <param name="response">The command response.</param>
    /// <param name="operationDescription">Description of the operation for error messages.</param>
    /// <returns>The data from a successful response.</returns>
    /// <exception cref="DriverException">Thrown when the response indicates failure.</exception>
    public static T EnsureSuccess<T>(this CommandResponse<T> response, string operationDescription = null)
    {
      if (response.Success)
        return response.Data;

      throw new DriverException(
          operationDescription != null
              ? $"{operationDescription} failed: {response.Error}"
              : response.Error ?? "Operation failed",
          response.ErrorCode,
          response.ErrorContext);
    }

    /// <summary>
    /// Ensures the command response was successful, otherwise throws an exception.
    /// </summary>
    /// <param name="response">The command response.</param>
    /// <param name="operationDescription">Description of the operation for error messages.</param>
    /// <exception cref="DriverException">Thrown when the response indicates failure.</exception>
    public static void EnsureSuccess(this CommandResponse<Unit> response, string operationDescription = null)
    {
      if (!response.Success)
      {
        throw new DriverException(
            operationDescription != null
                ? $"{operationDescription} failed: {response.Error}"
                : response.Error ?? "Operation failed",
            response.ErrorCode,
            response.ErrorContext);
      }
    }

    /// <summary>
    /// Maps a successful response to a different type.
    /// </summary>
    /// <typeparam name="T">The original response data type.</typeparam>
    /// <typeparam name="TResult">The new response data type.</typeparam>
    /// <param name="response">The command response.</param>
    /// <param name="mapper">Function to map the data.</param>
    /// <returns>A new command response with mapped data.</returns>
    public static CommandResponse<TResult> Map<T, TResult>(
        this CommandResponse<T> response,
        Func<T, TResult> mapper)
    {
      if (!response.Success)
      {
        return CommandResponse<TResult>.Fail(
            response.Error,
            response.ErrorCode,
            response.ErrorContext,
            response.ExitCode);
      }

      return CommandResponse<TResult>.Ok(mapper(response.Data), response.Output);
    }

    /// <summary>
    /// Adds additional error context to a failed response.
    /// </summary>
    /// <typeparam name="T">The response data type.</typeparam>
    /// <param name="response">The command response.</param>
    /// <param name="enrichContext">Action to enrich the error context.</param>
    /// <returns>The same response with enriched context.</returns>
    public static CommandResponse<T> EnrichContext<T>(
        this CommandResponse<T> response,
        Action<ErrorContext> enrichContext)
    {
      if (!response.Success && response.ErrorContext != null)
      {
        enrichContext(response.ErrorContext);
      }
      return response;
    }

    /// <summary>
    /// Tries to get the data from a response, returning a default if failed.
    /// </summary>
    /// <typeparam name="T">The response data type.</typeparam>
    /// <param name="response">The command response.</param>
    /// <param name="defaultValue">The default value if the response failed.</param>
    /// <returns>The response data or the default value.</returns>
    public static T GetOrDefault<T>(this CommandResponse<T> response, T defaultValue = default)
    {
      return response.Success ? response.Data : defaultValue;
    }

    /// <summary>
    /// Executes an action if the response was successful.
    /// </summary>
    /// <typeparam name="T">The response data type.</typeparam>
    /// <param name="response">The command response.</param>
    /// <param name="action">The action to execute.</param>
    /// <returns>The same response for chaining.</returns>
    public static CommandResponse<T> OnSuccess<T>(this CommandResponse<T> response, Action<T> action)
    {
      if (response.Success)
      {
        action(response.Data);
      }
      return response;
    }

    /// <summary>
    /// Executes an action if the response failed.
    /// </summary>
    /// <typeparam name="T">The response data type.</typeparam>
    /// <param name="response">The command response.</param>
    /// <param name="action">The action to execute.</param>
    /// <returns>The same response for chaining.</returns>
    public static CommandResponse<T> OnFailure<T>(this CommandResponse<T> response, Action<string, ErrorContext> action)
    {
      if (!response.Success)
      {
        action(response.Error, response.ErrorContext);
      }
      return response;
    }
  }
}
