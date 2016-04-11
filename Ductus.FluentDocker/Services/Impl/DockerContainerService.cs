using System;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerContainerService : ServiceBase, IContainerService
  {
    private readonly CertificatePaths _certificates;
    private readonly bool _removeOnDispose;
    private readonly bool _stopOnDispose;

    public DockerContainerService(string name, string id, Uri docker, ServiceRunningState state,
      CertificatePaths certificates,
      bool stopOnDispose = false, bool removeOnDispose = false) : base(name)
    {
      _certificates = certificates;
      _stopOnDispose = stopOnDispose;
      _removeOnDispose = removeOnDispose;

      Id = id;
      DockerHost = docker;
      State = state;
    }

    public string Id { get; }
    public Uri DockerHost { get; }
    public Container Configuration => DockerHost.InspectContainer(Id, _certificates).Data;

    public override void Dispose()
    {
      if (string.IsNullOrEmpty(Id) || null == DockerHost)
      {
        return;
      }

      if (_stopOnDispose)
      {
        DockerHost.Stop(Id, null, _certificates);
      }

      if (_removeOnDispose)
      {
        DockerHost.RemoveContainer(Id, false, true, null, _certificates);
      }
    }

    public override void Start()
    {
      State = ServiceRunningState.Starting;
      DockerHost.Start(Id, _certificates);
      if (Configuration.State.Running)
      {
        State = ServiceRunningState.Running;
      }
    }

    public override void Stop()
    {
      State = ServiceRunningState.Stopping;
      DockerHost.Stop(Id, null, _certificates);
      if (Configuration.State.Dead)
      {
        State = ServiceRunningState.Stopped;
      }
    }

    public override void Remove(bool force = false)
    {
      if (State != ServiceRunningState.Stopped)
      {
        State = ServiceRunningState.Stopping;
      }

      var result = DockerHost.RemoveContainer(Id, force, false, null, _certificates);
      if (result.Success)
      {
        State = ServiceRunningState.Stopped;
        State = ServiceRunningState.Removed;
      }
    }
  }
}