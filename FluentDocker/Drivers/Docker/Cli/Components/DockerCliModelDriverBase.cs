using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli.Binary;

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
    /// <param name="arguments">The full argument string (e.g. <c>model ls --json</c>).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The command result.</returns>
    protected virtual Task<SimpleCommandResult> RunAsync(string arguments, CancellationToken cancellationToken) =>
        ExecuteCommandAsync(arguments, cancellationToken);

    /// <summary>Runs a line-streamed <c>docker model …</c> command (overridable seam).</summary>
    /// <param name="arguments">The full argument string.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async stream of stdout lines.</returns>
    protected virtual IAsyncEnumerable<string> RunStreamingAsync(string arguments, CancellationToken cancellationToken) =>
        ExecuteStreamingCommandAsync(arguments, cancellationToken);
  }
}
