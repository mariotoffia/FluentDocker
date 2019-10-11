using System;

namespace Ductus.FluentDocker.Services.Impl
{
  public abstract class ServiceBase : IService
  {
    private readonly ServiceHooks _hooks = new ServiceHooks();
    private ServiceRunningState _state = ServiceRunningState.Unknown;

    protected ServiceBase(string name)
    {
      Name = name;
    }

    public abstract void Dispose();

    public string Name { get; }

    public ServiceRunningState State
    {
      get => _state;
      protected set
      {
        if (_state == value)
        {
          return;
        }

        _state = value;
        StateChange?.Invoke(this, new StateChangeEventArgs(this, value));
        _hooks.Execute(this, _state);
      }
    }

    public abstract void Start();
    public abstract void Pause();
    public abstract void Stop();
    public abstract void Remove(bool force = false);

    public IService AddHook(ServiceRunningState state, Action<IService> hook, string uniqueName = null)
    {
      _hooks.AddHook(uniqueName ?? Guid.NewGuid().ToString(), state, hook);
      return this;
    }

    public IService RemoveHook(string uniqueName)
    {
      _hooks.RemoveHook(uniqueName);
      return this;
    }

    public event ServiceDelegates.StateChange StateChange;
  }
}