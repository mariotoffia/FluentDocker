using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Kernel
{
  public partial class DriverRegistry
  {
    private const string RegistrationFailureDisposedInstanceKey =
        "FluentDocker.DriverRegistry.RegistrationFailureDisposedInstance";
    private static readonly ConditionalWeakTable<Exception, RegistrationFailureMarker>
        RegistrationFailureDisposedInstances = new();

    private void SetDefaultIfFirst(string driverId)
    {
      lock (_defaultDriverLock)
      {
        _defaultDriverId ??= driverId;
      }
    }

    private string? FirstRegisteredStillPresent()
    {
      return _registrationOrder.FirstOrDefault(id =>
          _drivers.ContainsKey(id) || _driverPacks.ContainsKey(id));
    }

    private DriverContext PrepareContext(string driverId, DriverContext context)
    {
      if (!string.IsNullOrEmpty(context.DriverId)
          && !string.Equals(context.DriverId, driverId, StringComparison.Ordinal))
        throw new DriverContextIdMismatchException(context.DriverId, driverId, nameof(context));

      var loggerFactory = IsNullLoggerFactory(context.LoggerFactory)
          ? _loggerFactory
          : context.LoggerFactory;
      return context.CloneWith(driverId, loggerFactory);
    }

    private void ThrowIfDriverIdUnavailable(string driverId, string kind)
    {
      if (_reservedDriverIds.Contains(driverId) ||
          _drivers.ContainsKey(driverId) ||
          _driverPacks.ContainsKey(driverId))
        throw new DriverException($"{kind} '{driverId}' is already registered", ErrorCodes.Driver.AlreadyRegistered);
    }

    private void ThrowIfDriverInstanceUnavailable(object driver, string kind)
    {
      if (_reservedDriverInstances.Contains(driver) ||
          _drivers.Values.Any(registration => ReferenceEquals(registration.Driver, driver)) ||
          _driverPacks.Values.Any(registration => ReferenceEquals(registration.DriverPack, driver)))
        throw new DriverException($"{kind} instance is already registered", ErrorCodes.Driver.AlreadyRegistered);
    }

    private static void ThrowIfDriverIdInvalid(string driverId)
    {
      if (string.IsNullOrWhiteSpace(driverId))
        throw new ArgumentException("Driver ID cannot be null or empty", nameof(driverId));
    }

    private DriverNotFoundException CreateDriverNotFoundException(string driverId) =>
        new(driverId, GetAllDriverIds());

    private async Task RollbackReservationAsync(string driverId, object driver)
    {
      await _registrationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
      try
      {
        _reservedDriverIds.Remove(driverId);
        _reservedDriverInstances.Remove(driver);
      }
      finally
      {
        _registrationLock.Release();
      }
    }

    internal static bool RegistrationFailureDisposedInstance(Exception exception) =>
        TryReadRegistrationFailureDisposedInstance(exception);

    private static void MarkFailureDisposedInstance(Exception exception)
    {
      RegistrationFailureDisposedInstances.GetValue(
          exception, static _ => new RegistrationFailureMarker());
      try
      {
        exception.Data[RegistrationFailureDisposedInstanceKey] = true;
      }
      catch
      {
      }
    }

    private static bool TryReadRegistrationFailureDisposedInstance(Exception exception)
    {
      if (RegistrationFailureDisposedInstances.TryGetValue(exception, out _))
        return true;

      try
      {
        return exception.Data[RegistrationFailureDisposedInstanceKey] is true;
      }
      catch
      {
        return false;
      }
    }

    private sealed class RegistrationFailureMarker
    {
    }

    private static bool IsNullLoggerFactory(ILoggerFactory loggerFactory)
    {
      return loggerFactory == null
             || ReferenceEquals(loggerFactory, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
    }

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private sealed class DriverRegistration
    {
      public IDriver Driver { get; set; } = null!;
      public DriverContext Context { get; set; } = null!;
      public DriverType Type { get; set; }
      public RuntimeType Runtime { get; set; }
    }

    private sealed class DriverPackRegistration
    {
      public IDriverPack DriverPack { get; set; } = null!;
      public DriverContext Context { get; set; } = null!;
      public DriverType Type { get; set; }
      public RuntimeType Runtime { get; set; }
    }
  }
}
