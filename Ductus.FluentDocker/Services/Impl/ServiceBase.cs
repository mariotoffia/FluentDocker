namespace Ductus.FluentDocker.Services.Impl
{
  public abstract class ServiceBase : IService
  {
    private ServiceRunningState _state = ServiceRunningState.Unknown;
    protected ServiceBase(string name)
    {
      Name = name;
    }

    public abstract void Dispose();

    public string Name { get; }

    public ServiceRunningState State
    {
      get { return _state; }
      set
      {
        if (_state == value)
        {
          return;
        }

        _state = value;
        StateChange?.Invoke(this, new StateChangeEventArgs(this, value));
      }
    }

    public abstract void Start();
    public abstract void Stop();
    public abstract void Remove(bool force = false);

    public event ServiceDelegates.StateChange StateChange;
  }
}
