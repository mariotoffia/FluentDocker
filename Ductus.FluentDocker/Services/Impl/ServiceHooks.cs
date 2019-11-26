using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Ductus.FluentDocker.Services.Impl
{
  public class ServiceHooks
  {
    private readonly ConcurrentDictionary<string, HookItem> _hooks = new ConcurrentDictionary<string, HookItem>();

    public void AddHook(string uniqueName, ServiceRunningState state, Action<IService> hook)
    {
      _hooks.TryAdd(uniqueName, new HookItem { State = state, Hook = hook });
    }

    public void RemoveHook(string uniqueName)
    {
      HookItem item;
      _hooks.TryRemove(uniqueName, out item);
    }

    public void Clear()
    {
      _hooks.Clear();
    }

    public void Execute(IService service, ServiceRunningState state)
    {
      foreach (var hook in _hooks.Values.Where(x => x.State == state))
      {
        hook.Hook(service);
      }
    }

    private class HookItem
    {
      internal Action<IService> Hook;
      internal ServiceRunningState State;
    }
  }
}
