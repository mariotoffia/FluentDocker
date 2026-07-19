using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Optional dictionary-backed <see cref="IDriverInterfaceResolver"/> helper for driver packs.
  /// It supplies only exact-type interface resolution over <see cref="Drivers"/>; it is NOT a
  /// full <see cref="IDriverPack"/>. Extenders that build a pack must implement
  /// <see cref="IDriverPack"/> themselves (Type/Runtime, InitializeAsync, GetCapabilitiesAsync,
  /// IsHealthyAsync) plus any disposal, on top of this resolver surface.
  /// </summary>
  /// <remarks>First-party packs intentionally implement their own resolution when they need behavior beyond this dictionary helper.</remarks>
  public abstract class DriverPackBase : IDriverInterfaceResolver
  {
    /// <summary>
    /// Type-to-implementation map for registered driver interfaces.
    /// </summary>
    private readonly Dictionary<Type, object> _drivers = [];

    /// <summary>
    /// Gets the driver dictionary for subclass use.
    /// </summary>
    /// <remarks>
    /// Driver packs populate this map during initialization and must not mutate it
    /// after <c>InitializeAsync</c> completes; resolution reads are intentionally unlocked.
    /// </remarks>
    protected Dictionary<Type, object> Drivers => _drivers;

    /// <summary>
    /// Registers a driver implementation by its interface type.
    /// </summary>
    protected void RegisterDriver<T>(T driver) where T : class
    {
      ArgumentNullException.ThrowIfNull(driver);
      Drivers[typeof(T)] = driver;
    }

    /// <summary>
    /// Whether this pack has been disposed. The base helper owns no disposable state and is
    /// never disposed itself, so it reports <c>false</c>. Extenders that add disposal must
    /// override this to gate resolution on their own disposed state: while <see cref="IsDisposed"/>
    /// is <c>true</c>, <see cref="TryResolve"/> throws <see cref="ObjectDisposedException"/>,
    /// honoring the post-disposal contract required by <see cref="IDriverPack"/>.
    /// </summary>
    protected virtual bool IsDisposed => false;

    /// <inheritdoc />
    /// <remarks>
    /// Extenders that add disposal should override <see cref="IsDisposed"/>; once it reports
    /// <c>true</c> this method throws <see cref="ObjectDisposedException"/> so resolution faults
    /// after disposal as the <see cref="IDriverPack"/> contract requires.
    /// </remarks>
    public virtual bool TryResolve(Type interfaceType, [NotNullWhen(true)] out object? implementation)
    {
      ArgumentNullException.ThrowIfNull(interfaceType);
      ObjectDisposedException.ThrowIf(IsDisposed, this);
      // A subclass can write null into Drivers directly (bypassing RegisterDriver); treat a
      // null-mapped interface as unsupported so the [NotNullWhen(true)] contract holds.
      return Drivers.TryGetValue(interfaceType, out implementation) && implementation is not null;
    }

    /// <inheritdoc />
    public virtual IReadOnlyCollection<Type> GetSupportedInterfaces()
    {
      return Drivers.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Tries to resolve a driver interface by exact generic type. A null-mapped or
    /// wrong-typed entry in <see cref="Drivers"/> is treated as unsupported.
    /// </summary>
    protected bool TryResolveSysCtl<T>([NotNullWhen(true)] out T? instance) where T : class
    {
      if (Drivers.TryGetValue(typeof(T), out var driver) && driver is T typed)
      {
        instance = typed;
        return true;
      }
      instance = null;
      return false;
    }
  }
}
