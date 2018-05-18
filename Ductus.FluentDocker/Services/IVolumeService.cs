using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services
{
    public interface IVolumeService : IService
    {
      DockerUri DockerHost { get; }
      ICertificatePaths Certificates { get; }
  }
}
