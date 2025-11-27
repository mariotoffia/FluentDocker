using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Volumes;

namespace FluentDocker.Services
{
  public interface IVolumeService : IService
  {
    DockerUri DockerHost { get; }
    ICertificatePaths Certificates { get; }

    Volume GetConfiguration(bool fresh = false);
  }
}
