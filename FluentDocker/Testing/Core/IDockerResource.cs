using System;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Represents an async-lifecycle Docker test resource.
  /// All test resources (containers, compose topologies, swarm stacks, etc.)
  /// implement this interface.
  /// </summary>
  public interface IDockerResource : IAsyncDisposable
  {
    /// <summary>
    /// Whether the resource has been successfully initialized and is ready for use.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Initializes the resource asynchronously: creates, starts, and waits for readiness.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
  }
}
