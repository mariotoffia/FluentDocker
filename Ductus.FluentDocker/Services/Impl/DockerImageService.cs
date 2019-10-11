using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerImageService : ServiceBase, IContainerImageService
  {
    private ImageConfig _containerConfigCache;
    private readonly bool _isWindowsHost;

    public DockerImageService(string name, string id, string tag, DockerUri dockerHost, ICertificatePaths certificate, bool isWindowsHost)
      : base(name)
    {
      _isWindowsHost = isWindowsHost;
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

    public override void Pause()
    {
      throw new FluentDockerNotSupportedException("Cannot pause a docker image service");
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
    public DockerUri DockerHost { get; }
    public ICertificatePaths Certificates { get; }

    public ImageConfig GetConfiguration(bool fresh = false)
    {
      if (!fresh && null != _containerConfigCache)
      {
        return _containerConfigCache;
      }

      _containerConfigCache = DockerHost.InspectImage(Id, Certificates).Data;
      return _containerConfigCache;
    }
  }
}