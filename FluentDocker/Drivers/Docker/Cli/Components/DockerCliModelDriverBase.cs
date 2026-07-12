using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Shared base for the Docker Model Runner CLI adapters. Extends
  /// <see cref="DockerCliDriverBase"/> and exposes two overridable seams
  /// (<see cref="RunAsync"/> / <see cref="RunStreamingAsync"/>) so command-string
  /// assembly and result mapping can be unit-tested without spawning a real
  /// <c>docker</c> process.
  /// </summary>
  public abstract class DockerCliModelDriverBase : DockerCliDriverBase
  {
    /// <summary>Initializes the base with a binary resolver.</summary>
    /// <param name="binaryResolver">The binary resolver.</param>
    protected DockerCliModelDriverBase(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <summary>Runs a <c>docker model …</c> command (overridable seam).</summary>
    /// <param name="context">Per-call driver context.</param>
    /// <param name="arguments">The full argument string (e.g. <c>model ls --json</c>).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The command result.</returns>
    protected virtual Task<SimpleCommandResult> RunAsync(DriverContext context, string arguments, CancellationToken cancellationToken) =>
        ExecuteCommandAsync(context, arguments, cancellationToken);

    /// <summary>Runs an unbounded <c>docker model …</c> command (overridable seam).</summary>
    /// <param name="context">Per-call driver context.</param>
    /// <param name="arguments">The full argument string.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The command result.</returns>
    protected virtual Task<SimpleCommandResult> RunUnboundedAsync(DriverContext context, string arguments, CancellationToken cancellationToken) =>
        ExecuteUnboundedCommandAsync(context, arguments, cancellationToken);

    /// <summary>Runs a line-streamed <c>docker model …</c> command (overridable seam).</summary>
    /// <param name="context">Per-call driver context.</param>
    /// <param name="arguments">The full argument string.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async stream of stdout lines.</returns>
    protected virtual IAsyncEnumerable<string> RunStreamingAsync(DriverContext context, string arguments, CancellationToken cancellationToken) =>
        ExecuteStreamingCommandAsync(context, arguments, cancellationToken);

    /// <summary>
    /// Runs a line-streamed <c>docker model …</c> command that emits progress on
    /// <b>stderr</b> (e.g. <c>docker model pull</c>), interleaving stdout and stderr
    /// into one sequence (overridable seam). Kept distinct from
    /// <see cref="RunStreamingAsync"/> so stdout-only consumers (logs) are unaffected.
    /// </summary>
    /// <param name="context">Per-call driver context.</param>
    /// <param name="arguments">The full argument string.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async stream of stdout and stderr lines, in arrival order.</returns>
    protected virtual IAsyncEnumerable<string> RunStreamingWithProgressAsync(DriverContext context, string arguments, CancellationToken cancellationToken) =>
        ExecuteStreamingCommandWithProgressAsync(context, arguments, cancellationToken);

    /// <summary>
    /// Resolves an error message for a failed <c>docker model</c> command, preferring
    /// stderr then stdout then <paramref name="fallback"/>, and rewriting it with an
    /// install hint when the failure indicates the Model plugin is missing.
    /// </summary>
    /// <param name="result">The completed command result.</param>
    /// <param name="fallback">Message to use if stderr and stdout are both empty.</param>
    /// <returns>A human-readable error message.</returns>
    protected static string ModelErrorOrDefault(SimpleCommandResult result, string fallback)
    {
      var error = FirstNonEmpty(result?.Error, result?.Output, fallback);
      return IsModelPluginMissing(error)
          ? $"The Docker Model plugin is not installed; install `docker-model-plugin` or enable Docker Desktop's Model Runner. ({error})"
          : error;
    }

    /// <summary>
    /// Resolves an error code for a failed <c>docker model</c> command from an exception:
    /// reuses an existing <see cref="DriverException.ErrorCode"/> (unless it is the generic
    /// command-execution-failed code) or falls back to classifying the exception's message.
    /// </summary>
    /// <param name="ex">The exception raised while running the command.</param>
    /// <param name="fallbackCode">Code to use if the message does not indicate a known failure.</param>
    /// <returns>An <see cref="ErrorCodes.Model"/>/<see cref="ErrorCodes"/> error code.</returns>
    protected static string ModelFailureCode(Exception ex, string fallbackCode) =>
        ex is DriverException driverException
            && !string.IsNullOrEmpty(driverException.ErrorCode)
            && driverException.ErrorCode != ErrorCodes.Driver.CommandExecutionFailed
            ? driverException.ErrorCode
            : ModelFailureCode(ex?.Message, fallbackCode);

    /// <summary>
    /// Resolves an error code for a failed <c>docker model</c> command from its error text:
    /// <see cref="ErrorCodes.Model.PluginMissing"/> when the plugin is not installed,
    /// otherwise the generic CLI classification via <see cref="DockerCliDriverBase.FailureCode(string, string)"/>.
    /// </summary>
    /// <param name="error">The captured error text.</param>
    /// <param name="fallbackCode">Code to use if the text does not match a known failure.</param>
    /// <returns>An error code.</returns>
    protected static string ModelFailureCode(string error, string fallbackCode) =>
        IsModelPluginMissing(error) ? ErrorCodes.Model.PluginMissing : FailureCode(error, fallbackCode);

    /// <summary>
    /// True if <paramref name="error"/> matches the Docker CLI's "is not a docker command"
    /// message, which is how the CLI reports that the <c>docker-model-plugin</c> is not installed.
    /// </summary>
    protected static bool IsModelPluginMissing(string error) =>
        !string.IsNullOrEmpty(error) &&
        error.Contains("is not a docker command", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the first non-blank value in <paramref name="values"/>, or <see cref="string.Empty"/> if none.</summary>
    protected static string FirstNonEmpty(params string[] values)
    {
      foreach (var value in values)
      {
        if (!string.IsNullOrWhiteSpace(value))
          return value;
      }

      return string.Empty;
    }
  }
}
