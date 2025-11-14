using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Model.Kernel
{
    /// <summary>
    /// Results from a BuildAsync() operation containing all built services.
    /// </summary>
    public class BuildResults : IAsyncDisposable, IDisposable
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

        /// <summary>
        /// Explicit async disposal method.
        /// </summary>
        public async Task DisposeAllAsync()
        {
            await DisposeAsync();
        }

        /// <summary>
        /// Sync disposal (calls async version).
        /// </summary>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
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
