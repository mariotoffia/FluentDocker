using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using FluentDocker.Common;
using FluentDocker.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Kernel
{
  // Resolution-only helpers behind SysCtl/TrySysCtl. Split out of FluentDockerKernel.cs to keep
  // that file under the 500-line limit (mirrors the DriverRegistry / DriverRegistry.Helpers.cs split).
  public partial class FluentDockerKernel
  {
    /// <summary>
    /// Resolves a driver interface by driver ID and runtime type.
    /// This is the unified resolution path used by all other SysCtl overloads.
    /// Resolution order:
    /// 1. Driver pack's IDriverInterfaceResolver.TryResolve.
    /// 2. Else delegate to the driver pack's ISysCtl.
    /// 3. If regular driver implements IDriverInterfaceResolver, ask it.
    /// 4. Else fallback to direct cast (driver is T).
    /// </summary>
    private bool TryResolveCore(
        string driverId,
        Type interfaceType,
        [NotNullWhen(true)] out object? resolved,
        out Exception? unsupportedCause,
        out Type? mismatchedType)
    {
      unsupportedCause = null;
      mismatchedType = null;
      if (_registry.TryGetDriverPack(driverId, out var driverPack))
      {
        try
        {
          if (driverPack.TryResolve(interfaceType, out resolved))
          {
            if (interfaceType.IsInstanceOfType(resolved))
              return true;
            // K-M4: a pack that resolved a non-null instance not assignable to the interface is a
            // mis-mapped registration — log it and report the actual type. A null "success" is
            // treated as plain unsupported (falls through to InterfaceNotSupportedException), never NRE.
            if (resolved != null)
              mismatchedType = LogTypeMismatch(driverPack, interfaceType, resolved);
          }
        }
        catch (InterfaceNotSupportedException ex)
        {
          unsupportedCause = ex;
        }
        catch (Exception ex) when (!IsResolutionContractException(ex))
        {
          LogPackFallbackFailure(driverPack, interfaceType, ex);
          throw CreateResolutionFailureException(driverId, interfaceType, ex);
        }

        // No pack.SysCtl(driverId) fallback: it was redundant with TryResolve above (both resolve
        // through the same interface map) and its only distinct effect — echoing driverId into an
        // exception — is meaningless at pack level, so IDriverPack no longer inherits ISysCtl
        // (KRN-MAJ-7). A genuine TryResolve fault is still surfaced HARD as DriverException above;
        // a soft InterfaceNotSupportedException still means "not implemented" (KRN-MAJ-1).
        resolved = null;
        return false;
      }

      if (_registry.TryGetDriver(driverId, out var driver))
      {
        if (driver is IDriverInterfaceResolver driverResolver)
        {
          try
          {
            if (driverResolver.TryResolve(interfaceType, out resolved))
            {
              if (interfaceType.IsInstanceOfType(resolved))
                return true;
              // K-M4 plain-driver path (previously silent). Null "success" → plain unsupported, not NRE.
              if (resolved != null)
                mismatchedType = LogTypeMismatch(driver, interfaceType, resolved);
            }
          }
          catch (InterfaceNotSupportedException ex)
          {
            unsupportedCause = ex;
          }
          catch (Exception ex) when (!IsResolutionContractException(ex))
          {
            throw CreateResolutionFailureException(driverId, interfaceType, ex);
          }
        }

        if (interfaceType.IsInstanceOfType(driver))
        {
          resolved = driver;
          return true;
        }

        resolved = null;
        return false;
      }

      throw new DriverNotFoundException(driverId, _registry.GetAllDriverIds());
    }

    private static bool IsResolutionContractException(Exception ex) =>
        ex is DriverException
        or IOException
        or ObjectDisposedException
        or OperationCanceledException;

    private static DriverException CreateResolutionFailureException(
        string driverId, Type interfaceType, Exception ex)
    {
      return new DriverException(
          $"Driver '{driverId}' failed while resolving interface '{TypeNameFormatter.Format(interfaceType)}'.",
          ex);
    }

    /// <summary>
    /// Logs (Warning — parity with <see cref="LogPackFallbackFailure"/>) a driver/pack that
    /// resolved an instance not assignable to the requested interface, and returns the actual
    /// resolved type so the caller can build an accurate <see cref="InterfaceNotSupportedException"/>
    /// message instead of a cause-less "does not implement" one (K-M4).
    /// </summary>
    private Type LogTypeMismatch(object owner, Type interfaceType, object resolved)
    {
      var actualType = resolved.GetType();
      _logger.LogWarning(
          "{OwnerType} resolved {ActualType} for requested interface {InterfaceType} but it is not assignable",
          owner.GetType().FullName,
          actualType.FullName,
          TypeNameFormatter.Format(interfaceType));
      return actualType;
    }

    private void LogPackFallbackFailure(IDriverPack driverPack, Type interfaceType, Exception ex)
    {
      _logger.LogWarning(
          ex,
          "Driver pack {DriverPackType} failed to resolve interface {InterfaceType}",
          driverPack.GetType().FullName,
          TypeNameFormatter.Format(interfaceType));
    }
  }
}
