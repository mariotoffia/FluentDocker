using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Optional interface for drivers and driver packs that support
  /// runtime resolution of arbitrary interface types.
  /// Implementing this allows a driver to expose custom interfaces
  /// without requiring kernel changes.
  /// </summary>
  public interface IDriverInterfaceResolver
  {
    /// <summary>
    /// Attempts to resolve an implementation for the given interface type.
    /// </summary>
    /// <param name="interfaceType">The interface type to resolve.</param>
    /// <param name="implementation">The resolved instance, or null.</param>
    /// <returns>True if the interface was resolved.</returns>
    /// <remarks>
    /// Implementer contract enforced by the kernel's resolution pipeline
    /// (<c>FluentDockerKernel.TryResolveCore</c>):
    /// <list type="bullet">
    /// <item>
    /// For an unsupported type, prefer returning <c>false</c>. Throwing
    /// <see cref="FluentDocker.Common.InterfaceNotSupportedException"/> is also treated as a soft
    /// miss (the kernel reports the interface as not implemented) and is equivalent to returning
    /// <c>false</c>.
    /// </item>
    /// <item>
    /// Any OTHER exception is a hard resolution failure: the kernel wraps it in a
    /// <see cref="FluentDocker.Common.DriverException"/> (except the pass-through contract
    /// exceptions <see cref="FluentDocker.Common.DriverException"/>, <see cref="System.IO.IOException"/>,
    /// <see cref="ObjectDisposedException"/>, and <see cref="OperationCanceledException"/>, which
    /// propagate as-is). Do not throw arbitrary exceptions to signal "unsupported".
    /// </item>
    /// <item>
    /// When returning <c>true</c>, <paramref name="implementation"/> must be non-null and
    /// assignable to <paramref name="interfaceType"/>. A returned instance that is not assignable
    /// is rejected (logged as a warning) and treated as a soft miss; a <c>true</c> return with a
    /// null instance is likewise treated as unsupported.
    /// </item>
    /// <item>
    /// The kernel does not null-check <paramref name="interfaceType"/> before calling; a null value
    /// is not expected and implementations should throw <see cref="ArgumentNullException"/> (the
    /// dictionary-backed <see cref="DriverPackBase"/> does).
    /// </item>
    /// <item>
    /// After the resolver's owner is disposed, this method should throw
    /// <see cref="ObjectDisposedException"/>; the kernel propagates it to the caller.
    /// </item>
    /// </list>
    /// </remarks>
    bool TryResolve(Type interfaceType, [NotNullWhen(true)] out object? implementation);

    /// <summary>
    /// Gets all interface types supported by this resolver.
    /// </summary>
    IReadOnlyCollection<Type> GetSupportedInterfaces();
  }
}
