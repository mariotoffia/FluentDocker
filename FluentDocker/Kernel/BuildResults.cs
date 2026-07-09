#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Services;

namespace FluentDocker.Kernel
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
    /// Per-service wall-clock budget (milliseconds) bounding <b>asynchronous</b> disposal
    /// of each service (containers, pods, networks, volumes, compose), so one hung
    /// daemon call cannot consume cleanup time for later resources. The synchronous
    /// <see cref="Dispose"/> path does not apply this budget.
    /// </summary>
    public const int DefaultDisposeBudgetMs = 60_000;

    private readonly List<BuildScope> _scopes = scopes == null ? [] : [.. scopes];
    private int _disposed; // 0=not disposed/incomplete, 1=disposing, 2=complete

    /// <summary>
    /// Gets all services across all scopes.
    /// </summary>
    /// <remarks>After complete disposal this returns empty; if cleanup was incomplete,
    /// undisposed services remain observable here for retry.</remarks>
    public IReadOnlyList<IServiceAsync> All =>
        [.. _scopes.SelectMany(s => s.Results)];

    /// <summary>
    /// Gets services for a specific driver.
    /// </summary>
    /// <param name="driverId">Driver identifier</param>
    /// <returns>Services for the specified driver</returns>
    /// <remarks>After complete disposal this returns empty; if cleanup was incomplete,
    /// undisposed services remain observable here for retry.</remarks>
    public IReadOnlyList<IServiceAsync> ForDriver(string driverId) =>
        [.. _scopes
            .Where(s => s.DriverId == driverId)
            .SelectMany(s => s.Results)];

    /// <summary>
    /// Gets all scopes.
    /// </summary>
    /// <remarks>After complete disposal, scope result collections are empty snapshots;
    /// if cleanup was incomplete, scopes retain undisposed services.</remarks>
    public IReadOnlyList<BuildScope> Scopes => _scopes;

    /// <summary>
    /// Gets all container services across all scopes.
    /// </summary>
    /// <remarks>After complete disposal this returns empty; if cleanup was incomplete,
    /// undisposed services remain observable here for retry.</remarks>
    public IReadOnlyList<IContainerService> Containers =>
        [.. All.OfType<IContainerService>()];

    /// <summary>
    /// Gets all network services across all scopes.
    /// </summary>
    /// <remarks>After complete disposal this returns empty; if cleanup was incomplete,
    /// undisposed services remain observable here for retry.</remarks>
    public IReadOnlyList<INetworkService> Networks =>
        [.. All.OfType<INetworkService>()];

    /// <summary>
    /// Gets all volume services across all scopes.
    /// </summary>
    /// <remarks>After complete disposal this returns empty; if cleanup was incomplete,
    /// undisposed services remain observable here for retry.</remarks>
    public IReadOnlyList<IVolumeService> Volumes =>
        [.. All.OfType<IVolumeService>()];

    /// <summary>
    /// Gets all compose services across all scopes.
    /// </summary>
    /// <remarks>After complete disposal this returns empty; if cleanup was incomplete,
    /// undisposed services remain observable here for retry.</remarks>
    public IReadOnlyList<IComposeService> ComposeServices =>
        [.. All.OfType<IComposeService>()];

    /// <summary>
    /// Gets a container service by name.
    /// </summary>
    /// <param name="name">Container name</param>
    /// <returns>Container service or null if not found</returns>
    public IContainerService? GetContainer(string name) =>
        Containers.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.Ordinal) ||
            string.Equals(c.Name?.TrimStart('/'), name, StringComparison.Ordinal));

    /// <summary>
    /// Gets a network service by name.
    /// </summary>
    /// <param name="name">Network name</param>
    /// <returns>Network service or null if not found</returns>
    public INetworkService? GetNetwork(string name) =>
        Networks.FirstOrDefault(n =>
            string.Equals(n.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Gets a volume service by name.
    /// </summary>
    /// <param name="name">Volume name</param>
    /// <returns>Volume service or null if not found</returns>
    public IVolumeService? GetVolume(string name) =>
        Volumes.FirstOrDefault(v =>
            string.Equals(v.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Gets all services of a specific type.
    /// </summary>
    /// <typeparam name="T">Service type</typeparam>
    /// <returns>Services of the specified type</returns>
    /// <remarks>After complete disposal this returns empty; if cleanup was incomplete,
    /// undisposed services remain observable here for retry.</remarks>
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
    /// best-effort and never throws: each <see cref="BuildScope"/> logs its own failures, and
    /// each service receives its own <see cref="DefaultDisposeBudgetMs"/> budget so a hung daemon
    /// call cannot starve later resources. Services are removed only after disposal completes;
    /// failed or timed-out resources remain observable in <see cref="All"/>, and a later disposal
    /// call retries them. If a second <see cref="DisposeAsync"/> call arrives while the first
    /// teardown is still running, the second call returns immediately and does not await the
    /// in-flight teardown; callers that need teardown completion must await the first call.
    /// </remarks>
    [SuppressMessage("Usage", "CA1816",
        Justification = "Disposal supports retry: SuppressFinalize is deferred to CompleteOrAllowRetry and only runs once teardown fully completes.")]
    public async ValueTask DisposeAsync()
    {
      if (Volatile.Read(ref _disposed) == 2 ||
          Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
      {
        return;
      }

      try
      {
        var perServiceTimeout = TimeSpan.FromMilliseconds(DefaultDisposeBudgetMs);
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
          await _scopes[i].DisposeAllAsync(perServiceTimeout).ConfigureAwait(false);
        }

        CompleteOrAllowRetry();
      }
      catch
      {
        Interlocked.Exchange(ref _disposed, 0);
        throw;
      }
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
    [SuppressMessage("Usage", "CA1816",
        Justification = "Disposal supports retry: SuppressFinalize is deferred to CompleteOrAllowRetry and only runs once teardown fully completes.")]
    public void Dispose()
    {
      if (Volatile.Read(ref _disposed) == 2 ||
          Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
      {
        return;
      }

      try
      {
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
          _scopes[i].DisposeAll();
        }

        CompleteOrAllowRetry();
      }
      catch
      {
        Interlocked.Exchange(ref _disposed, 0);
        throw;
      }
    }

    /// <summary>
    /// Explicit sync disposal method.
    /// </summary>
    public void DisposeAll()
    {
      Dispose();
    }

    [SuppressMessage("Usage", "CA1816",
        Justification = "Intentional: SuppressFinalize runs here (not in Dispose/DisposeAsync) so it only fires once retry-able teardown fully completes.")]
    private void CompleteOrAllowRetry()
    {
      if (_scopes.All(scope => scope.Results.Count == 0))
      {
        Interlocked.Exchange(ref _disposed, 2);
        GC.SuppressFinalize(this);
        return;
      }

      Interlocked.Exchange(ref _disposed, 0);
    }
  }
}
