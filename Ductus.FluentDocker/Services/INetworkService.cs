using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Networks;

namespace Ductus.FluentDocker.Services
{
  public interface INetworkService : IService
  {
    string Id { get; }
    DockerUri DockerHost { get; }
    ICertificatePaths Certificates { get; }
    NetworkConfiguration GetConfiguration(bool fresh = false);

    INetworkService Attach(IContainerService container, bool detatchOnDisposeNetwork);
    INetworkService Attach(string containerId, bool detatchOnDisposeNetwork);
    INetworkService Detatch(IContainerService container, bool force = false);
    INetworkService Detatch(string containerId, bool force = false);
  }
}
