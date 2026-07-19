using System;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  // ponytail: shared so services invoke StateChange handlers OUTSIDE _stateLock (7.1);
  // a reentrant handler that waits on a lifecycle op would otherwise deadlock on the lock.
  internal static class StateChangeNotifier
  {
    internal static void Invoke(
        ServiceDelegates.StateChange handlers,
        StateChangeEventArgs args,
        ILogger logger,
        string serviceName)
    {
      foreach (ServiceDelegates.StateChange handler in handlers.GetInvocationList())
      {
        try
        {
          handler(args.Service, args);
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "{Service} state change handler failed", serviceName);
        }
      }
    }
  }
}
