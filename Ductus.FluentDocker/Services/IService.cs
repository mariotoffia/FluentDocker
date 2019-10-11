using System;
using Ductus.FluentDocker.Common;

namespace Ductus.FluentDocker.Services
{
  public sealed class ServiceDelegates
  {
    public delegate void StateChange(object sender, StateChangeEventArgs evt);
  }

  public interface IService : IDisposable
  {
    string Name { get; }
    ServiceRunningState State { get; }
    /// <summary>
    /// Starts a service either from scratch or un-pause the service if earlier paused by <see cref="Pause"/>.
    /// </summary>
    void Start();
    /// <summary>
    /// Pauses the service (if it supports such) and may be resumed by <see cref="Start"/>.
    /// </summary>
    /// <exception cref="FluentDockerNotSupportedException">If any the service do not support this operation.</exception>
    /// <remarks>
    /// Some services may implement this functionality and some it makes no sense or it is impossible
    /// to pause the service. When the service is paused it will be reflected as <see cref="ServiceRunningState.Paused"/>
    /// in the <see cref="State"/> property.
    /// </remarks>
    void Pause();
    void Stop();
    void Remove(bool force = false);
    IService AddHook(ServiceRunningState state, Action<IService> hook, string uniqueName = null);
    IService RemoveHook(string uniqueName);

    event ServiceDelegates.StateChange StateChange;
  }
}