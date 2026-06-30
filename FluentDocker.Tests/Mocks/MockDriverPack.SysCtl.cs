using System;
using FluentDocker.Common;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// ISysCtl type-based resolution for MockDriverPack.
  /// </summary>
  public partial class MockDriverPack
  {
    /// <inheritdoc />
    public object SysCtl(string driverId, Type interfaceType)
    {
      if (!_initialized)
        throw new InvalidOperationException("MockDriverPack not initialized. Call InitializeAsync first.");
      if (_drivers.TryGetValue(interfaceType, out var driver))
        return driver;
      throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
    }

    /// <inheritdoc />
    public bool TrySysCtl<T>(string driverId, out T instance) where T : class
    {
      if (!_initialized)
        throw new InvalidOperationException("MockDriverPack not initialized. Call InitializeAsync first.");
      if (_drivers.TryGetValue(typeof(T), out var driver))
      {
        instance = (T)driver;
        return true;
      }
      instance = null!; // interface declares a non-nullable out
      return false;
    }
  }
}
