using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerVolumeService : ServiceBase, IVolumeService
  {
    public DockerVolumeService(string name, DockerUri host, ICertificatePaths certificates) : base(name)
    {
      DockerHost = host;
      Certificates = certificates;
    }

    public override void Dispose()
    {
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
      State = ServiceRunningState.Removed;
    }

    public DockerUri DockerHost { get; }
    public ICertificatePaths Certificates { get; }
  }
}