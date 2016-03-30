using System;
using System.Collections.Generic;
using System.IO;
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
        if (_prms.StopContainerOnDispose || _prms.RemoveContainerOnDispose)
        {
          _client.StopContainer(_container.Id);
        }

        if (_prms.RemoveContainerOnDispose)
        {
          _client.RemoveContainer(_container.Id);
        }
      }

      _client.Dispose();

      if (null != _prms.VolumesToRemoveOnDispose)
      {
        foreach (string path in _prms.VolumesToRemoveOnDispose)
        {
          Directory.Delete(path,true/*recursive*/);
        }
      }
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

      if (!string.IsNullOrEmpty(_prms.PortToWaitOn))
      {
        WaitForPort(GetHostPort(_prms.PortToWaitOn), _prms.PortWaitTimeout);
      }

      if (!string.IsNullOrEmpty(_prms.WaitForProcess))
      {
        WaitForProcess(_prms.WaitForProcess, _prms.ProcessWaitTimeout);
      }

      return this;
    }

    private void WaitForProcess(string process, long millisTimeout)
    {
      do
      {
        try
        {
          var proc = ContainerProcesses();
          if (null != proc.Rows && proc.Rows.Any(x => x.Command == process))
          {
            return;
          }
        }
        catch (Exception)
        {
          // Ignore
        }

        Thread.Sleep(1000);
      } while (millisTimeout > 0);

      throw new FluentDockerException($"Timeout while waiting for process {process}");
    }

    public string GetHostVolume(string name)
    {
      return _prms.Volumes.FirstOrDefault(x => x.Name == name)?.Host.ToPlatformPath();
    }

    public string ContainerName => _settings.Name;

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

    public Processes ContainerProcesses(string args = null)
    {
      try
      {
        var res =
          _client.Containers.ListProcessesAsync(_container.Id, new ListProcessesParameters {PsArgs = args}).Result;

        var processes = new Processes {Columns = res.Titles, Rows = new List<ProcessRow>()};
        foreach (var row in res.Processes)
        {
          processes.Rows.Add(ProcessRow.ToRow(res.Titles,row));
        }

        return processes;
      }
      catch (Exception)
      {
        return new Processes();
      }
    }

    private void WaitForPort(int port, long millisTimeout)
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
        Binds = DockerVolumeMount.ToStringArray(prms.Volumes),
        Links = prms.links.Count == 0 ? null : prms.links
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
          vols.Add(mount.Docker, new object());
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