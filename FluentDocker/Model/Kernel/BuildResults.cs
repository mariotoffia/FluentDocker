using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Services;

namespace FluentDocker.Model.Kernel
{
  /// <summary>
  /// Results from a BuildAsync() operation containing all built services.
  /// </summary>
  /// <remarks>
  /// Creates build results from a list of scopes.
  /// </remarks>
  public class BuildResults(List<BuildScope> scopes) : IAsyncDisposable, IDisposable
  {
    /// <summary>
    /// Total wall-clock budget (milliseconds) bounding the <b>asynchronous</b> disposal of ALL
    /// services across all scopes (containers, pods, networks, volumes, compose), so a single hung
    /// daemon cannot block <see cref="DisposeAsync"/> indefinitely. The synchronous
    /// <see cref="Dispose"/> path does not apply this budget.
    /// </summary>
    public const int DefaultDisposeBudgetMs = 60_000;

    private readonly List<BuildScope> _scopes = scopes ?? [];
    private int _disposed;

    /// <summary>
    /// Gets all services across all scopes.
    /// </summary>
    public IReadOnlyList<IServiceAsync> All =>
        [.. _scopes.SelectMany(s => s.Results)];

    /// <summary>
    /// Gets services for a specific driver.
    /// </summary>
    /// <param name="driverId">Driver identifier</param>
    /// <returns>Services for the specified driver</returns>
    public IReadOnlyList<IServiceAsync> ForDriver(string driverId) =>
        [.. _scopes
            .Where(s => s.DriverId == driverId)
            .SelectMany(s => s.Results)];

    /// <summary>
    /// Gets all scopes.
    /// </summary>
    public IReadOnlyList<BuildScope> Scopes => _scopes;

    /// <summary>
    /// Gets all container services across all scopes.
    /// </summary>
    public IReadOnlyList<IContainerService> Containers =>
        [.. All.OfType<IContainerService>()];

    /// <summary>
    /// Gets all network services across all scopes.
    /// </summary>
    public IReadOnlyList<INetworkService> Networks =>
        [.. All.OfType<INetworkService>()];

    /// <summary>
    /// Gets all volume services across all scopes.
    /// </summary>
    public IReadOnlyList<IVolumeService> Volumes =>
        [.. All.OfType<IVolumeService>()];

    /// <summary>
    /// Gets all compose services across all scopes.
    /// </summary>
    public IReadOnlyList<IComposeService> ComposeServices =>
        [.. All.OfType<IComposeService>()];

    /// <summary>
    /// Gets a container service by name.
    /// </summary>
    /// <param name="name">Container name</param>
    /// <returns>Container service or null if not found</returns>
    public IContainerService GetContainer(string name) =>
        Containers.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Name?.TrimStart('/'), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets a network service by name.
    /// </summary>
    /// <param name="name">Network name</param>
    /// <returns>Network service or null if not found</returns>
    public INetworkService GetNetwork(string name) =>
        Networks.FirstOrDefault(n =>
            string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets a volume service by name.
    /// </summary>
    /// <param name="name">Volume name</param>
    /// <returns>Volume service or null if not found</returns>
    public IVolumeService GetVolume(string name) =>
        Volumes.FirstOrDefault(v =>
            string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets all services of a specific type.
    /// </summary>
    /// <typeparam name="T">Service type</typeparam>
    /// <returns>Services of the specified type</returns>
    public IReadOnlyList<T> OfType<T>() where T : IServiceAsync =>
        [.. All.OfType<T>()];

    /// <summary>
    /// Async disposal of all services.
    /// </summary>
    /// <remarks>
    /// Scopes (and the services within them) are disposed in reverse creation order, so that
    /// dependents (e.g. containers) are torn down before their dependencies (e.g. the networks
    /// and volumes they are attached to). This is a best-effort heuristic — reverse creation
    /// order (idiomatic declare-before-use), not a topological dependency sort. Disposal is
    /// best-effort and never throws: each <see cref="BuildScope"/> logs its own failures, and a
    /// single <see cref="DefaultDisposeBudgetMs"/> budget bounds the total time so a hung daemon
    /// cannot block teardown forever.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
      {
        return;
      }

      using var cts = new CancellationTokenSource(DefaultDisposeBudgetMs);

      for (var i = _scopes.Count - 1; i >= 0; i--)
      {
        await _scopes[i].DisposeAllAsync(cts.Token).ConfigureAwait(false);
      }

      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Explicit async disposal method.
    /// </summary>
    public async Task DisposeAllAsync()
    {
      await DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Sync disposal. Disposes scopes in reverse creation order; best-effort and never throws.
    /// </summary>
    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
      {
        return;
      }

      for (var i = _scopes.Count - 1; i >= 0; i--)
      {
        _scopes[i].DisposeAll();
      }

      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Explicit sync disposal method.
    /// </summary>
    public void DisposeAll()
    {
      Dispose();
    }
  }
}
