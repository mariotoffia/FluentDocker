using System;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Services
{
    /// <summary>
    /// Engine scope type for Windows/Linux daemon switching.
    /// </summary>
    public enum EngineScopeType
    {
        Unknown = 0,
        Windows = 1,
        Linux = 2
    }

    /// <summary>
    /// Async interface for switching Docker daemon between Windows and Linux modes.
    /// This is primarily for Docker Desktop on Windows.
    /// </summary>
#if NETSTANDARD2_0
    public interface IEngineScope : IDisposable
#else
    public interface IEngineScope : IDisposable, IAsyncDisposable
#endif
    {
        /// <summary>
        /// The current scope/mode of the engine.
        /// </summary>
        EngineScopeType Scope { get; }

        /// <summary>
        /// Checks if the current engine is Windows-based.
        /// </summary>
        Task<bool> IsWindowsEngineAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the current engine is Linux-based.
        /// </summary>
        Task<bool> IsLinuxEngineAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Switches to Linux daemon mode.
        /// </summary>
        /// <returns>True if successful or already in Linux mode.</returns>
        Task<bool> UseLinuxAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Switches to Windows daemon mode.
        /// </summary>
        /// <returns>True if successful or already in Windows mode.</returns>
        Task<bool> UseWindowsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Disposes the scope asynchronously, restoring the original engine mode if changed.
        /// </summary>
#if NETSTANDARD2_0
        Task DisposeAsync();
#endif
    }
}

