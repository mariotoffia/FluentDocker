using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Volumes;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerVolumeService : ServiceBase, IVolumeService
  {
    private Volume _config;
    private readonly bool _removeOnDispose;

    public DockerVolumeService(string name, DockerUri host, ICertificatePaths certificates, bool removeOnDispose) : base(name)
    {
      DockerHost = host;
      Certificates = certificates;
      _removeOnDispose = removeOnDispose;
    }

    public override void Dispose()
    {
      if (_removeOnDispose)
      {
        Remove(force: true);
      }
    }

    public override void Start()
    {
      State = ServiceRunningState.Starting;
      State = ServiceRunningState.Running;
    }

    public override void Pause()
    {
      throw new FluentDockerNotSupportedException("Cannot pause a docker volume service");
    }

    public override void Stop()
    {
      State = ServiceRunningState.Stopping;
      State = ServiceRunningState.Stopped;
    }

    public override void Remove(bool force = false)
    {
      State = ServiceRunningState.Removing;
      DockerHost.VolumeRm(Certificates, force, Name);
      State = ServiceRunningState.Removed;
    }

    public DockerUri DockerHost { get; }
    public ICertificatePaths Certificates { get; }
    public Volume GetConfiguration(bool fresh = false)
    {
      if (!fresh && null != _config)
        return _config;

      var result = DockerHost.VolumeInspect(certificates: Certificates, volume: Name);
      if (!result.Success)
        throw new FluentDockerException($"Could not run docker volume inspect for volume name: {Name}");

      return _config = result.Data[0];
    }
  }
}