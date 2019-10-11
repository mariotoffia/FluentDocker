using System.Collections.Generic;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Networks;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerNetworkService : ServiceBase, INetworkService
  {
    private readonly HashSet<string> _detatchOnDispose = new HashSet<string>();
    private readonly bool _removeOnDispose;
    private NetworkConfiguration _config;

    public DockerNetworkService(string name, string id, DockerUri dockerHost, ICertificatePaths certificate,
      bool removeOnDispose = false) :
      base(name)
    {
      _removeOnDispose = removeOnDispose;
      Id = id;
      DockerHost = dockerHost;
      Certificates = certificate;
    }

    public string Id { get; }
    public DockerUri DockerHost { get; }
    public ICertificatePaths Certificates { get; }

    public NetworkConfiguration GetConfiguration(bool fresh = false)
    {
      if (!fresh && null != _config)
        return _config;

      var result = DockerHost.NetworkInspect(certificates: Certificates, network: Id);
      if (!result.Success)
        throw new FluentDockerException($"Could not run docker network inspect on network id: {Id}");

      return _config = result.Data;
    }

    public INetworkService Attach(IContainerService container, bool detatchOnDisposeNetwork)
    {
      return Attach(container.Id, detatchOnDisposeNetwork);
    }

    public INetworkService Attach(string containerId, bool detatchOnDisposeNetwork)
    {
      if (detatchOnDisposeNetwork)
        _detatchOnDispose.Add(containerId);

      DockerHost.NetworkConnect(containerId, Id, certificates: Certificates);
      return this;
    }

    public INetworkService Detatch(IContainerService container, bool force = false)
    {
      return Detatch(container.Id, force);
    }

    public INetworkService Detatch(string containerId, bool force = false)
    {
      _detatchOnDispose.Remove(containerId);
      DockerHost.NetworkDisconnect(containerId, Id, force, Certificates);
      return this;
    }

    public override void Dispose()
    {
      foreach (var containerId in _detatchOnDispose)
        DockerHost.NetworkDisconnect(containerId, Id, true, Certificates);

      if (_removeOnDispose)
        Remove(true);
    }

    public override void Start()
    {
    }

    public override void Pause()
    {
      throw new FluentDockerNotSupportedException("Cannot pause a docker network service");
    }

    public override void Stop()
    {
    }

    public override void Remove(bool force = false)
    {
      var result = DockerHost.NetworkRm(Certificates, network: Id);
      if (!result.Success)
        throw new FluentDockerException($"Failed to do docker network rm {Id}");
    }
  }
}