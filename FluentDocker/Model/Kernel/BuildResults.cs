using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Services;

namespace FluentDocker.Model.Kernel
{
    /// <summary>
    /// Results from a BuildAsync() operation containing all built services.
    /// </summary>
#if NETSTANDARD2_0
    public class BuildResults : IDisposable
#else
    public class BuildResults : IAsyncDisposable, IDisposable
#endif
    {
        private readonly List<BuildScope> _scopes;

        /// <summary>
        /// Creates build results from a list of scopes.
        /// </summary>
        public BuildResults(List<BuildScope> scopes)
        {
            _scopes = scopes ?? new List<BuildScope>();
        }

        /// <summary>
        /// Gets all services across all scopes.
        /// </summary>
        public IReadOnlyList<IService> All =>
            _scopes.SelectMany(s => s.Results).ToList();

        /// <summary>
        /// Gets services for a specific driver.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>Services for the specified driver</returns>
        public IReadOnlyList<IService> ForDriver(string driverId) =>
            _scopes
                .Where(s => s.DriverId == driverId)
                .SelectMany(s => s.Results)
                .ToList();

        /// <summary>
        /// Gets all scopes.
        /// </summary>
        public IReadOnlyList<BuildScope> Scopes => _scopes;

        /// <summary>
        /// Async disposal of all services.
        /// </summary>
#if !NETSTANDARD2_0
        public async ValueTask DisposeAsync()
        {
            foreach (var service in All)
            {
                if (service is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    service?.Dispose();
                }
            }
        }
#endif

        /// <summary>
        /// Explicit async disposal method.
        /// </summary>
        public async Task DisposeAllAsync()
        {
#if NETSTANDARD2_0
            foreach (var service in All)
            {
                service?.Dispose();
            }
            await Task.CompletedTask;
#else
            await DisposeAsync();
#endif
        }

        /// <summary>
        /// Sync disposal (calls async version).
        /// </summary>
        public void Dispose()
        {
#if NETSTANDARD2_0
            foreach (var service in All)
            {
                service?.Dispose();
            }
#else
            DisposeAsync().AsTask().GetAwaiter().GetResult();
#endif
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
