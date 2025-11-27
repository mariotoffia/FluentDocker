using System;
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

    INetworkService Attach(IContainerService container, bool detatchOnDisposeNetwork, string alias = null);
    INetworkService Attach(string containerId, bool detatchOnDisposeNetwork, string alias = null);
    [Obsolete("Please use the properly spelled `Detach` method instead.")]
    INetworkService Detatch(IContainerService container, bool force = false);
    INetworkService Detach(IContainerService container, bool force = false);
    [Obsolete("Please use the properly spelled `Detach` method instead.")]
    INetworkService Detatch(string containerId, bool force = false);
    INetworkService Detach(string containerId, bool force = false);
  }
}
