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
    /// <summary>The daemon scope could not be determined.</summary>
    Unknown = 0,

    /// <summary>The daemon is running Windows containers.</summary>
    Windows = 1,

    /// <summary>The daemon is running Linux containers.</summary>
    Linux = 2
  }

  /// <summary>
  /// Async interface for switching Docker daemon between Windows and Linux modes.
  /// This is primarily for Docker Desktop on Windows.
  /// </summary>
  public interface IEngineScope : IDisposable, IAsyncDisposable
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
  }
}
