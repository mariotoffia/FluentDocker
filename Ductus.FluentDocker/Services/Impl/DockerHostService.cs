using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Machines;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerHostService : ServiceBase, IHostService
  {
    internal const string DockerCertPath = "DOCKER_CERT_PATH";
    internal const string DockerTlsVerify = "DOCKER_TLS_VERIFY";

    private const string DefaultCaCertName = "ca.pem";
    private const string DefaultClientCertName = "cert.pem";
    private const string DefaultClientKeyName = "key.pem";
    private readonly bool _isWindowsHost;

    private readonly bool _stopWhenDisposed;

    public DockerHostService(string name, bool isNative, bool stopWhenDisposed = false, string dockerUri = null,
      string certificatePath = null, bool isWindowsHost = false)
      : base(name)
    {
      _isWindowsHost = isWindowsHost;
      _stopWhenDisposed = stopWhenDisposed;

      IsNative = isNative;
      if (IsNative)
      {
        var uri = dockerUri ?? DockerUri.GetDockerHostEnvronmentPathOrDefault();
        var certPath = certificatePath ?? Environment.GetEnvironmentVariable(DockerCertPath);

        if (!string.IsNullOrEmpty(certPath))
          Certificates = new CertificatePaths
          {
            CaCertificate = Path.Combine(certPath, DefaultCaCertName),
            ClientCertificate = Path.Combine(certPath, DefaultClientCertName),
            ClientKey = Path.Combine(certPath, DefaultClientKeyName)
          };

        Host = string.IsNullOrEmpty(uri) ? null : new DockerUri(uri);
        RequireTls = Environment.GetEnvironmentVariable(DockerTlsVerify) == "1";
        State = ServiceRunningState.Running;
        return;
      }

      // Machine - do inspect & get url
      MachineSetup(name);
    }

    public override void Dispose()
    {
      if (_stopWhenDisposed && !IsNative)
        Name.Stop();
    }

    public override void Start()
    {
      if (IsNative)
        throw new InvalidOperationException($"Cannot start docker host {Name} since it is native");

      if (State != ServiceRunningState.Stopped)
        throw new InvalidOperationException($"Cannot start docker host {Name} since it has state {State}");

      var response = Name.Start();
      if (!response.Success)
        throw new InvalidOperationException($"Could not start docker host {Name}");

      if (!IsNative)
        MachineSetup(Name);
    }

    public override void Stop()
    {
      if (!IsNative)
        throw new InvalidOperationException($"Cannot stop docker host {Name} since it is native");

      if (State != ServiceRunningState.Running)
        throw new InvalidOperationException($"Cannot stop docker host {Name} since it has state {State}");

      var response = Name.Stop();
      if (!response.Success)
        throw new InvalidOperationException($"Could not stop docker host {Name}");
    }

    public override void Remove(bool force = false)
    {
      if (!IsNative)
        throw new InvalidOperationException($"Cannot remove docker host {Name} since it is native");

      if (State == ServiceRunningState.Running && !force)
        throw new InvalidOperationException(
          $"Cannot remove docker host {Name} since it has state {State} and force is not enabled");

      var response = Name.Delete(force);
      if (!response.Success)
        throw new InvalidOperationException($"Could not remove docker host {Name}");
    }

    public DockerUri Host { get; private set; }
    public bool IsNative { get; }
    public bool RequireTls { get; private set; }
    public ICertificatePaths Certificates { get; private set; }

    public IList<IContainerService> GetRunningContainers()
    {
      return GetContainers(false);
    }

    public IList<IContainerService> GetContainers(bool all = true, string filter = null)
    {
      var options = string.Empty;
      if (all)
        options += " --all";

      if (!string.IsNullOrEmpty(filter))
        options += $" --filter {filter}";

      var result = Host.Ps(options, Certificates);
      if (!result.Success)
        return new List<IContainerService>();

      return (from id in result.Data
        let config = Host.InspectContainer(id, Certificates).Data
        select
          new DockerContainerService(config.Name, id, Host, config.State.ToServiceState(),
            Certificates, isWindowsContainer: _isWindowsHost)).Cast<IContainerService>().ToList();
    }

    public IList<IContainerImageService> GetImages(bool all = true, string filer = null)
    {
      var images = Host.Images(null, Certificates);
      if (!images.Success)
        return new List<IContainerImageService>();

      return
        images.Data.Select(image =>
            new DockerImageService(image.Name, image.Id, image.Tags[0], Host, Certificates, _isWindowsHost))
          .Cast<IContainerImageService>()
          .ToList();
    }

    public IContainerService Create(string image, ContainerCreateParams prms = null,
      bool stopOnDispose = true, bool deleteOnDispose = true, bool deleteVolumeOnDispose = false,
      bool deleteNamedVolumeOnDispose = false,
      string command = null, string[] args = null)
    {
      var res = Host.Create(image, command, args, prms, Certificates);

      if (!res.Success || 0 == res.Data.Length)
        throw new FluentDockerException(
          $"Could not create Service from {image} with command {command}, args {args}, and parameters {prms}. Result: {res}");

      var config = Host.InspectContainer(res.Data, Certificates);
      if (!config.Success)
        throw new FluentDockerException(
          $"Could not return service for docker id {res.Data} - Container was created, you have to manually delete it or do a Ls");

      return new DockerContainerService(config.Data.Name.Substring(1), res.Data, Host,
        config.Data.State.ToServiceState(),
        Certificates, stopOnDispose, deleteOnDispose, deleteVolumeOnDispose, deleteNamedVolumeOnDispose,
        _isWindowsHost);
    }

    public MachineConfiguration GetMachineConfiguration()
    {
      return Name.Inspect().Data;
    }

    private void MachineSetup(string name)
    {
      State = name.Status();
      if (State != ServiceRunningState.Running)
        return;

      Host = name.Uri();

      var info = name.Inspect().Data;
      RequireTls = info.RequireTls;

      ResolveCertificatePaths(info);
    }

    private void ResolveCertificatePaths(MachineConfiguration info)
    {
      var storePath = info.AuthConfig.StorePath;

      var caCertPath = Path.Combine(storePath, DefaultCaCertName);
      var clientCertPath = Path.Combine(storePath, DefaultClientCertName);
      var clientKeyPath = Path.Combine(storePath, DefaultClientKeyName);

      if (File.Exists(clientCertPath) && File.Exists(caCertPath) && File.Exists(clientKeyPath))
      {
        // Check if ca, client and key is in the store path
        // if so use those instead.
        Certificates = new CertificatePaths
        {
          CaCertificate = info.AuthConfig.CaCertPath,
          ClientCertificate = info.AuthConfig.ClientCertPath,
          ClientKey = info.AuthConfig.ClientKeyPath
        };
        return;
      }

      // Otherwise use the defaults
      Certificates = new CertificatePaths
      {
        CaCertificate = info.AuthConfig.CaCertPath,
        ClientCertificate = info.AuthConfig.ClientCertPath,
        ClientKey = info.AuthConfig.ClientKeyPath
      };
    }
  }
}