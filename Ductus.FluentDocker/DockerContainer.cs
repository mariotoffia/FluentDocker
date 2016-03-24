using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Docker.DotNet;
using Docker.DotNet.Models;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Internal;

namespace Ductus.FluentDocker
{
  public class DockerContainer : IDisposable
  {
    private readonly DockerClient _client;
    private readonly DockerParams _prms;
    private CreateContainerResponse _container;
    private ContainerResponse _settings;

    internal DockerContainer(DockerParams prms)
    {
      _prms = prms;

      var certificates = new DockerCertificates(_prms.DockerCertPath);

      _client =
        new DockerClientConfiguration(new Uri(_prms.DockerHost),
          new DockerCertificateCredentials(certificates.CaCertificate, certificates.ClientCertificate)).CreateClient();
    }

    public string Host => _client.Configuration.EndpointBaseUri.Host;

    public void Dispose()
    {
      if (_container != null)
      {
        _client.StopContainer(_container.Id);
        _client.RemoveContainer(_container.Id);
      }

      _client.Dispose();
    }

    public DockerContainer Create()
    {
      if (null == _container)
      {
        _container = _client.CreateContainer(ToParameters(_prms), true);
      }

      return this;
    }

    public DockerContainer Start()
    {
      if (null == _container)
      {
        Create();
      }

      if (null == _container)
      {
        throw new FluentDockerException("Could not create container");
      }

      // ReSharper disable once PossibleNullReferenceException
      _client.StartContainer(_container.Id, ToHostConfig(_prms));
      _settings = _client.Containers.InspectContainerAsync(_container.Id).Result;

      return !string.IsNullOrEmpty(_prms.PortToWaitOn)
        ? WaitForPort(GetHostPort(_prms.PortToWaitOn), _prms.WaitTimeout)
        : this;
    }

    public int GetHostPort(string containerPort)
    {
      var portBindings = _settings.NetworkSettings.Ports;
      if (!portBindings.ContainsKey(containerPort))
      {
        return -1;
      }

      var portBinding = portBindings[containerPort];
      if (0 == portBinding.Count)
      {
        return -1;
      }

      return int.Parse(portBinding[0].HostPort);
    }

    private DockerContainer WaitForPort(int port, long millisTimeout)
    {
      using (var s = new Socket(SocketType.Stream, ProtocolType.Tcp))
      {
        long totalWait = 0;
        while (totalWait < millisTimeout)
        {
          try
          {
            s.Connect(Host, port);
            break;
          }
          catch (Exception)
          {
            Thread.Sleep(1000);
            totalWait += 1000;
            if (totalWait >= millisTimeout)
            {
              throw new Exception($"Timeout waiting for port {port}");
            }
          }
        }
      }
      return this;
    }

    private static HostConfig ToHostConfig(DockerParams prms)
    {
      IDictionary<string, IList<PortBinding>> ports = null;
      if (null != prms.Ports && 0 != prms.Ports.Length)
      {
        ports = prms.Ports.ToDictionary<string, string, IList<PortBinding>>(port => port,
          port => new List<PortBinding> {new PortBinding {HostIp = "0.0.0.0", HostPort = ""}});
      }

      return new HostConfig
      {
        PortBindings = ports,
        Binds = DockerVolumeMount.ToStringArray(prms.Volumes)
      };
    }

    private static CreateContainerParameters ToParameters(DockerParams prms)
    {
      IDictionary<string, object> ports = null;
      if (null != prms.Ports && 0 != prms.Ports.Length)
      {
        ports = prms.Ports.ToDictionary<string, string, object>(port => port,
          port => new PortBinding {HostIp = "0.0.0.0", HostPort = ""});
      }

      IDictionary<string, object> vols = null;
      if (null != prms.Volumes && 0 != prms.Volumes.Length)
      {
        vols = new Dictionary<string, object>();
        foreach (var mount in prms.Volumes)
        {
          vols.Add(mount.Host, new object());
        }
      }

      return new CreateContainerParameters
      {
        ContainerName = prms.ContainerName,
        Config = new Config
        {
          Env = prms.Env,
          Image = prms.ImageName,
          Cmd = null == prms.Cmd ? null : new List<string>(prms.Cmd),
          DomainName = prms.DomainName,
          User = prms.User,
          Hostname = prms.HostName,
          NetworkDisabled = false,
          ExposedPorts = ports,
          Volumes = vols
        }
      };
    }
  }
}