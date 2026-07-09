using System;
using System.Linq;
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
    private void SetDefaultIfFirst(string driverId)
    {
      lock (_defaultDriverLock)
      {
        _defaultDriverId ??= driverId;
      }
    }

    private string FirstRegisteredStillPresent()
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

    private DriverNotFoundException CreateDriverNotFoundException(string driverId) =>
        new(driverId, GetAllDriverIds());

    private async Task RollbackReservationAsync(string driverId)
    {
      await _registrationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
      try
      {
        _reservedDriverIds.Remove(driverId);
      }
      finally
      {
        _registrationLock.Release();
      }
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
      public IDriver Driver { get; set; }
      public DriverContext Context { get; set; }
      public DriverType Type { get; set; }
      public RuntimeType Runtime { get; set; }
    }

    private sealed class DriverPackRegistration
    {
      public IDriverPack DriverPack { get; set; }
      public DriverContext Context { get; set; }
      public DriverType Type { get; set; }
      public RuntimeType Runtime { get; set; }
    }
  }
}
