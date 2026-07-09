using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Services
{
  /// <summary>
  /// Async pod service interface (Podman-specific).
  /// </summary>
  public interface IPodService : IServiceAsync
  {
    /// <summary>
    /// Pod identifier.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Stops the pod with a configurable Podman stop timeout in seconds.
    /// Use <see cref="IServiceAsync.StopAsync(CancellationToken)"/> for the 10-second default.
    /// </summary>
    /// <param name="timeoutSeconds">Seconds Podman gives pod containers to stop before killing them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopAsync(int timeoutSeconds, CancellationToken cancellationToken = default);
  }
}
