using System;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerImageService : ServiceBase, IContainerImageService
  {
    private Container _containerConfigCache;

    public DockerImageService(string name, string id, string tag, Uri dockerHost, ICertificatePaths certificate)
      : base(name)
    {
      Id = id;
      Tag = tag;
      Certificates = certificate;
      DockerHost = dockerHost;
      State = ServiceRunningState.Running;
    }

    public override void Dispose()
    {
      Stop();
    }

    public override void Start()
    {
      State = ServiceRunningState.Starting;
      State = ServiceRunningState.Running;
    }

    public override void Stop()
    {
      State = ServiceRunningState.Stopping;
      State = ServiceRunningState.Stopped;
    }

    public override void Remove(bool force = false)
    {
      State = ServiceRunningState.Removing;
      // TODO: Remove image
      State = ServiceRunningState.Removed;
    }

    public string Id { get; }
    public string Tag { get; }
    public Uri DockerHost { get; }
    public ICertificatePaths Certificates { get; }

    public Container GetConfiguration(bool fresh = false)
    {
      if (!fresh && null != _containerConfigCache)
      {
        return _containerConfigCache;
      }

      _containerConfigCache = DockerHost.InspectContainer(Id, Certificates).Data;
      return _containerConfigCache;
    }
  }
}