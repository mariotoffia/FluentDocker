using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Kernel
{
    /// <summary>
    /// Registry for managing driver and driver pack instances.
    /// </summary>
    public interface IDriverRegistry
    {
        #region Driver Registration

        /// <summary>
        /// Registers a driver with a unique ID.
        /// </summary>
        /// <param name="driverId">Unique driver identifier</param>
        /// <param name="driver">Driver instance</param>
        /// <param name="context">Driver context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task RegisterAsync(string driverId, IDriver driver, DriverContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// Unregisters a driver.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        void Unregister(string driverId);

        /// <summary>
        /// Gets a driver by ID.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>Driver instance</returns>
        /// <exception cref="Common.DriverNotFoundException">If driver not found</exception>
        IDriver GetDriver(string driverId);

        /// <summary>
        /// Tries to get a driver by ID.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <param name="driver">Output driver instance</param>
        /// <returns>True if driver found</returns>
        bool TryGetDriver(string driverId, out IDriver driver);

        #endregion

        #region Driver Pack Registration

        /// <summary>
        /// Registers a driver pack with a unique ID.
        /// </summary>
        /// <param name="driverId">Unique driver identifier</param>
        /// <param name="driverPack">Driver pack instance</param>
        /// <param name="context">Driver context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task RegisterDriverPackAsync(string driverId, IDriverPack driverPack, DriverContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a driver pack by ID.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>Driver pack instance</returns>
        /// <exception cref="Common.DriverNotFoundException">If driver pack not found</exception>
        IDriverPack GetDriverPack(string driverId);

        /// <summary>
        /// Tries to get a driver pack by ID.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <param name="driverPack">Output driver pack instance</param>
        /// <returns>True if driver pack found</returns>
        bool TryGetDriverPack(string driverId, out IDriverPack driverPack);

        /// <summary>
        /// Checks if a driver ID refers to a driver pack.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>True if the ID refers to a driver pack</returns>
        bool IsDriverPack(string driverId);

        #endregion

        #region Context and Status

        /// <summary>
        /// Gets the driver context for a driver or driver pack.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>Driver context</returns>
        DriverContext GetContext(string driverId);

        /// <summary>
        /// Checks if a driver or driver pack is registered.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>True if registered</returns>
        bool IsRegistered(string driverId);

        /// <summary>
        /// Gets all registered driver IDs (both drivers and driver packs).
        /// </summary>
        IReadOnlyList<string> GetAllDriverIds();

        #endregion

        #region Filtering

        /// <summary>
        /// Gets all drivers and driver packs of a specific type.
        /// </summary>
        /// <param name="driverType">Driver type filter</param>
        /// <returns>List of matching driver IDs</returns>
        IReadOnlyList<string> GetDriversByType(DriverType driverType);

        /// <summary>
        /// Gets all drivers and driver packs for a specific runtime.
        /// </summary>
        /// <param name="runtime">Runtime type filter</param>
        /// <returns>List of matching driver IDs</returns>
        IReadOnlyList<string> GetDriversByRuntime(RuntimeType runtime);

        #endregion

        #region Default Driver

        /// <summary>
        /// Gets the default driver ID (if set).
        /// </summary>
        string GetDefaultDriverId();

        /// <summary>
        /// Sets the default driver ID.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        void SetDefaultDriver(string driverId);

        #endregion
    }
}
