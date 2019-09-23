using System;

namespace Ductus.FluentDocker.Services
{
  public enum EngineScopeType
  {
    Unknown  = 0,
    Windows = 1,
    Linux = 2
  }
  
  /// <summary>
  /// Set the docker target engine (if windows) to either Windows or Linux.
  /// </summary>
  public interface IEngineScope : IDisposable
  {
    EngineScopeType Scope { get; }
  }
}