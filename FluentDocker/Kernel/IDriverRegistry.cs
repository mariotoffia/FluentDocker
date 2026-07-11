using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Registry for managing driver and driver pack instances.
  /// </summary>
  /// <remarks>
  /// Implementations own registered driver lifetimes and must make
  /// <see cref="IAsyncDisposable.DisposeAsync"/> idempotent; the kernel may retry
  /// disposal after a timeout. Prefer async disposal/unregistration. Any sync bridge
  /// should document its sync-over-async trade-off and avoid single-threaded contexts.
  /// </remarks>
  public interface IDriverRegistry : IAsyncDisposable
  {
    #region Driver Registration

    /// <summary>
    /// Registers a driver with a unique ID.
    /// </summary>
    /// <param name="driverId">Unique driver identifier</param>
    /// <param name="driver">Driver instance</param>
    /// <param name="context">Driver context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <remarks>On failure after acceptance begins, the registry disposes the supplied instance; do not reuse or re-dispose it.</remarks>
    Task RegisterAsync(string driverId, IDriver driver, DriverContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a driver or driver pack and disposes the removed instance.
    /// </summary>
    /// <param name="driverId">Driver identifier</param>
    /// <remarks>
    /// The registry owns registered driver lifetimes. If the removed driver was
    /// the default, the earliest-registered driver still present becomes the default (null if none remain).
    /// </remarks>
    /// <exception cref="Common.DriverNotFoundException">If driver or driver pack not found.</exception>
    void Unregister(string driverId);

    /// <summary>
    /// Asynchronously unregisters a driver or driver pack and disposes the removed instance.
    /// </summary>
    /// <param name="driverId">Driver identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <remarks>
    /// The registry owns registered driver lifetimes. If the removed driver was
    /// the default, the earliest-registered driver still present becomes the default (null if none remain).
    /// </remarks>
    /// <exception cref="Common.DriverNotFoundException">If driver or driver pack not found.</exception>
    Task UnregisterAsync(string driverId, CancellationToken cancellationToken = default);

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
    bool TryGetDriver(string driverId, [NotNullWhen(true)] out IDriver? driver);

    #endregion

    #region Driver Pack Registration

    /// <summary>
    /// Registers a driver pack with a unique ID.
    /// </summary>
    /// <param name="driverId">Unique driver identifier</param>
    /// <param name="driverPack">Driver pack instance</param>
    /// <param name="context">Driver context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <remarks>On failure after acceptance begins, the registry disposes the supplied instance; do not reuse or re-dispose it.</remarks>
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
    bool TryGetDriverPack(string driverId, [NotNullWhen(true)] out IDriverPack? driverPack);

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

    #region Disposal diagnostics

    /// <summary>
    /// Number of driver/pack instances abandoned because their disposal exceeded the teardown
    /// budget. A non-zero value after disposal means OS processes/containers may have leaked and
    /// warrants investigation. Observable without a downcast so consumers can detect leaks after a
    /// timed-out teardown (KRN-MAJ-2).
    /// </summary>
    /// <remarks>
    /// Read alongside <see cref="IsDisposeComplete"/>: the disposal pass can finish
    /// (<see cref="IsDisposeComplete"/> is <c>true</c>) while this is non-zero, because
    /// budget-exhausted drivers/packs are abandoned rather than retried indefinitely.
    /// </remarks>
    int AbandonedDriverCount { get; }

    /// <summary>
    /// <c>true</c> once the disposal pass has completed and the registry is no longer mid-teardown.
    /// This does <em>not</em> mean every driver/pack was cleanly disposed — budget-exhausted
    /// instances are abandoned rather than retried, and disposal still completes around them.
    /// </summary>
    /// <remarks>
    /// Check <see cref="AbandonedDriverCount"/> alongside this property to detect that kind of
    /// partial cleanup; <c>IsDisposeComplete == true</c> with a non-zero
    /// <see cref="AbandonedDriverCount"/> means the pass finished but something leaked.
    /// </remarks>
    bool IsDisposeComplete { get; }

    #endregion
  }
}
