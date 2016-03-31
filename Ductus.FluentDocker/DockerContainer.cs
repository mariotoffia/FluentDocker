using System;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// Member variable to be used when <see cref="DockerParams.ExportContainerHostPath"/> is set in the
    /// <see cref="Dispose"/> method to export the container if not client has marked <see cref="Success"/>.
    /// </summary>
    private bool _exceptionOccured = true;

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

    public DockerContainer Success()
    {
      _exceptionOccured = false;
      return this;
    }

    public void Dispose()
    {
      if (_container != null)
      {
        if (_prms.CopyFilesWhenDisposed.Count > 0)
        {
          foreach (var copy in _prms.CopyFilesWhenDisposed)
          {
            _client.CopyFromContainer(_container.Id, copy.Item2, copy.Item3);
          }
        }

        if (_prms.StopContainerOnDispose || _prms.RemoveContainerOnDispose)
        {
          _client.StopContainer(_container.Id);
        }

        if (!string.IsNullOrEmpty(_prms.ExportContainerHostPath) && _exceptionOccured)
        {
          // Not marked Success and export is wanted
          ExportContainer(_prms.ExportContainerHostPath, _prms.ExportContainerHostPathExplode);
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
        _client.WaitForProcess(_container.Id, _prms.WaitForProcess, _prms.ProcessWaitTimeout);
      }

      if (_prms.CopyFilesAfterStart.Count > 0)
      {
        foreach (var copy in _prms.CopyFilesAfterStart)
        {
          _client.CopyFromContainer(_container.Id, copy.Item2, copy.Item3);
        }
      }

      return this;
    }

    public string GetHostVolume(string name)
    {
      return _prms.Volumes.FirstOrDefault(x => x.Name == name)?.Host.ToPlatformPath();
    }

    public string GetHostCopyPath(string name)
    {
      var path = (from item in _prms.CopyFilesAfterStart where item.Item1 == name select item.Item3).FirstOrDefault();
      if (null != path)
      {
        return path;
      }

      return (from item in _prms.CopyFilesWhenDisposed where item.Item1 == name select item.Item3).FirstOrDefault();
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
    ///   Exports the container as a tar file or exploded on host filesystem.
    /// </summary>
    /// <param name="hostFilePath">The target hostfilepath to export to (if explode is set to true).</param>
    /// <param name="explode">Will extract the container onto the <paramref name="hostFilePath" />.</param>
    /// <returns>Either the tar file full path or the hostfile path to where the container was extracted to.</returns>
    /// <remarks>
    ///   If <paramref name="explode" /> is set to false. The returning path is a completely different path than the
    ///   in-parameter
    ///   <paramref name="hostFilePath" />.
    /// </remarks>
    public string ExportContainer(string hostFilePath, bool explode = true)
    {
      return _client.ExportContainer(_container.Id, hostFilePath, explode);
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