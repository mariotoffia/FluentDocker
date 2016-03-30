using System;
using System.IO;
using System.Linq;
using System.Threading;
using Docker.DotNet;
using Docker.DotNet.Models;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Internal;
using SharpCompress.Common;
using SharpCompress.Reader;

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

    public string ContainerName => _settings.Name;

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
        foreach (var path in _prms.VolumesToRemoveOnDispose)
        {
          Directory.Delete(path, true /*recursive*/);
        }
      }
    }

    public DockerContainer Create()
    {
      if (null == _container)
      {
        _container = _client.CreateContainer(_prms.ToParameters(), true);
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

      _client.StartContainer(_container.Id, _prms.ToHostConfig());
      _settings = _client.Containers.InspectContainerAsync(_container.Id).Result;

      if (!string.IsNullOrEmpty(_prms.PortToWaitOn))
      {
        Host.WaitForPort(GetHostPort(_prms.PortToWaitOn), _prms.PortWaitTimeout);
      }

      if (!string.IsNullOrEmpty(_prms.WaitForProcess))
      {
        _client.WaitForProcess(_prms.WaitForProcess, _prms.ProcessWaitTimeout);
      }

      return this;
    }

    public string GetHostVolume(string name)
    {
      return _prms.Volumes.FirstOrDefault(x => x.Name == name)?.Host.ToPlatformPath();
    }

    public int GetHostPort(string containerPort)
    {
      return _settings.GetHostPort(containerPort);
    }

    public Processes ContainerProcesses(string args = null)
    {
      return _client.ContainerProcesses(_container.Id, args);
    }

    /// <summary>
    ///   Copies a file or subdirectory from the container and stored on the host file path.
    /// </summary>
    /// <param name="containerFilePath">The container file or path to directory to copy.</param>
    /// <param name="hostFilePath">The host filepath to copy the contents to. This may contain template parameters.</param>
    /// <returns>The path to where the docker files and directories are copied to. If fails null is returned.</returns>
    public string Copy(string containerFilePath, string hostFilePath)
    {
      return _client.CopyFromContainer(_container.Id, containerFilePath, hostFilePath);
    }
  }
}