using System;

namespace Ductus.FluentDocker.Services
{
  public enum EngineScopeType
  {
    Unknown = 0,
    Windows = 1,
    Linux = 2
  }

  /// <summary>
  /// Set the docker target engine (if windows) to either Windows or Linux.
  /// </summary>
  public interface IEngineScope : IDisposable
  {
    /// <summary>
    /// The current scope in the engine scope
    /// </summary>
    EngineScopeType Scope { get; }
    /// <summary>
    /// Manually alter the scope to be linux
    /// </summary>
    /// <returns>If successful or if not altered it returns true, false otherwise</returns>
    bool UseLinux();
    /// <summary>
    /// Manually alter the scope to be windows
    /// </summary>
    /// <returns>If successful or if not altered it returns true, false otherwise</returns>
    bool UseWindows();
  }
}
