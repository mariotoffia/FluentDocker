using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Volumes;

namespace Ductus.FluentDocker.Services
{
    public interface IVolumeService : IService
    {
      DockerUri DockerHost { get; }
      ICertificatePaths Certificates { get; }

      Volume GetConfiguration(bool fresh = false);
  }
}
