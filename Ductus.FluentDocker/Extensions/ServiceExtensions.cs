using System;
using System.Threading;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Extensions
{
  public static class ServiceExtensions
  {
    private static void WaitForState(this IContainerService container, ServiceRunningState state)
    {
      WaitForStateObject so;
      using (var mre = new ManualResetEventSlim())
      {
        so = new WaitForStateObject {Mre = mre, Container = container, State = state};
        using (new Timer(Callback, so, 0, 500))
        {
          mre.Wait();
        }
      }

      if (so.Exception != null)
        throw so.Exception;
    }

    private static void Callback(object state)
    {
      var obj = (WaitForStateObject) state;
      var containerState = obj.Container.GetConfiguration(true).State;
      if (!string.IsNullOrWhiteSpace(containerState.Error))
      {
        obj.Exception = new FluentDockerException($"Unable to start container: {containerState.Error}");
        obj.Mre.Set();
      }

      if (containerState.ToServiceState() == obj.State)
        obj.Mre.Set();
    }

    public static void WaitForRunning(this IContainerService container)
    {
      WaitForState(container, ServiceRunningState.Running);
    }

    public static void WaitForStopped(this IContainerService container)
    {
      WaitForState(container, ServiceRunningState.Stopped);
    }

    private sealed class WaitForStateObject
    {
      public ManualResetEventSlim Mre { get; set; }
      public IContainerService Container { get; set; }
      public ServiceRunningState State { get; set; }
      public Exception Exception { get; set; }
    }
  }
}