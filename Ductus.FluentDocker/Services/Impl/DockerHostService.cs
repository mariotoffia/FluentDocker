using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Machines;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerHostService : ServiceBase, IHostService
  {
    internal const string DockerHost = "DOCKER_HOST";
    internal const string DockerCertPath = "DOCKER_CERT_PATH";
    internal const string DockerTlsVerify = "DOCKER_TLS_VERIFY";

    private const string DefaultCaCertName = "ca.pem";
    private const string DefaultClientCertName = "cert.pem";
    private const string DefaultClientKeyName = "key.pem";

    private readonly bool _stopWhenDisposed;
    private string _caCertPath;
    private string _clientCertPath;
    private string _clientKeyPath;

    public DockerHostService(string name, bool isNative, bool stopWhenDisposed = false, string dockerUri = null,
      string certificatePath = null)
      : base(name)
    {
      _stopWhenDisposed = stopWhenDisposed;

      IsNative = isNative;
      if (IsNative)
      {
        var uri = dockerUri ?? Environment.GetEnvironmentVariable(DockerHost);
        if (string.IsNullOrEmpty(uri))
        {
          throw new ArgumentException($"DockerHostService cannot be native when {DockerHost} is not defined",
            nameof(isNative));
        }

        var certPath = certificatePath ?? Environment.GetEnvironmentVariable(DockerCertPath);
        if (string.IsNullOrEmpty(certPath))
        {
          throw new ArgumentException($"DockerHostService cannot be native when {DockerCertPath} is not defined",
            nameof(isNative));
        }

        Host = new Uri(uri);
        RequireTls = Environment.GetEnvironmentVariable(DockerTlsVerify) == "1";
        ClientCaCertificate = certPath.ToCertificate(DefaultCaCertName);
        ClientCertificate = certPath.ToCertificate(DefaultClientCertName, DefaultClientKeyName);
        State = ServiceRunningState.Running;

        _caCertPath = Path.Combine(certPath, DefaultCaCertName);
        _clientCertPath = Path.Combine(certPath, DefaultClientCertName);
        _clientKeyPath = Path.Combine(certPath, DefaultClientKeyName);
        return;
      }

      // Machine - do inspect & get url
      MachineSetup(name);
    }

    public override void Dispose()
    {
      if (_stopWhenDisposed && !IsNative)
      {
        Name.Stop();
      }
    }

    public override void Start()
    {
      if (IsNative)
      {
        throw new InvalidOperationException($"Cannot start docker host {Name} since it is native");
      }

      if (State != ServiceRunningState.Stopped)
      {
        throw new InvalidOperationException($"Cannot start docker host {Name} since it has state {State}");
      }

      var response = Name.Start();
      if (!response.Success)
      {
        throw new InvalidOperationException($"Could not start docker host {Name}");
      }

      if (!IsNative)
      {
        MachineSetup(Name);
      }
    }

    public override void Stop()
    {
      if (!IsNative)
      {
        throw new InvalidOperationException($"Cannot stop docker host {Name} since it is native");
      }

      if (State != ServiceRunningState.Running)
      {
        throw new InvalidOperationException($"Cannot stop docker host {Name} since it has state {State}");
      }

      var response = Name.Stop();
      if (!response.Success)
      {
        throw new InvalidOperationException($"Could not stop docker host {Name}");
      }
    }

    public override void Remove(bool force = false)
    {
      if (!IsNative)
      {
        throw new InvalidOperationException($"Cannot remove docker host {Name} since it is native");
      }

      if (State == ServiceRunningState.Running && !force)
      {
        throw new InvalidOperationException(
          $"Cannot remove docker host {Name} since it has state {State} and force is not enabled");
      }

      var response = Name.Delete(force);
      if (!response.Success)
      {
        throw new InvalidOperationException($"Could not remove docker host {Name}");
      }
    }

    public Uri Host { get; private set; }
    public bool IsNative { get; }
    public bool RequireTls { get; private set; }
    public X509Certificate2 ClientCertificate { get; }
    public X509Certificate2 ClientCaCertificate { get; private set; }

    public IList<IContainerService> RunningContainers => GetContainers(false);

    public IList<IContainerService> GetContainers(bool all = true, string filter = null)
    {
      var options = string.Empty;
      if (all)
      {
        options += " --all";
      }

      if (!string.IsNullOrEmpty(filter))
      {
        options += $" --filter={filter}";
      }

      var result = Host.Ps(options, _caCertPath, _clientCertPath, _clientKeyPath);
      if (!result.Success)
      {
        return new List<IContainerService>();
      }

      return (from id in result.Data
        let config = Host.InspectContainer(id, _caCertPath, _clientCertPath, _clientKeyPath).Data
        select
          new DockerContainerService(config.Name, id, Host, config.State.ToServiceState(),
            new CertificatePaths
            {
              CaCertificate = _caCertPath,
              ClientKey = _clientKeyPath,
              ClientCertificate = _clientCertPath
            })).Cast<IContainerService>().ToList();
    }

    public IContainerService Create(string image, ContainerCreateParams prms = null,
      bool stopOnDispose = true, bool deleteOnDispose = true,
      string command = null, string[] args = null)
    {
      var res = Host.Create(image, command, args, prms, new CertificatePaths
      {
        CaCertificate = _caCertPath,
        ClientKey = _clientKeyPath,
        ClientCertificate = _clientCertPath
      });

      if (!res.Success || 0 == res.Data.Length)
      {
        throw new FluentDockerException(
          $"Could not create Service from {image} with command {command}, args {args}, and parameters {prms}. Result: {res}");
      }

      var certificates = new CertificatePaths
      {
        CaCertificate = _caCertPath,
        ClientKey = _clientKeyPath,
        ClientCertificate = _clientCertPath
      };

      var config = Host.InspectContainer(res.Data, certificates);
      if (!config.Success)
      {
        throw new FluentDockerException(
          $"Could not return service for docker id {res.Data} - Container was created, you have to manually delete it or do a Ls");
      }

      return new DockerContainerService(config.Data.Name.Substring(1), res.Data, Host,
        config.Data.State.ToServiceState(),
        certificates, stopOnDispose, deleteOnDispose);
    }

    public MachineConfiguration GetMachineConfiguration()
    {
      return Name.Inspect().Data;
    }

    private void MachineSetup(string name)
    {
      State = name.Status();
      if (State != ServiceRunningState.Running)
      {
        return;
      }

      Host = name.Uri();

      var info = name.Inspect().Data;
      RequireTls = info.RequireTls;

      ResolveCertificatePaths(info);

      ClientCaCertificate =
        Path.GetDirectoryName(_caCertPath).ToCertificate(Path.GetFileName(_caCertPath));

      ClientCaCertificate =
        Path.GetDirectoryName(_clientCertPath)
          .ToCertificate(Path.GetFileName(_clientCertPath),
            Path.GetFileName(_clientKeyPath));
    }

    private void ResolveCertificatePaths(MachineConfiguration info)
    {
      var storePath = info.AuthConfig.StorePath;

      _caCertPath = Path.Combine(storePath, DefaultCaCertName);
      _clientCertPath = Path.Combine(storePath, DefaultClientCertName);
      _clientKeyPath = Path.Combine(storePath, DefaultClientKeyName);

      if (File.Exists(_clientCertPath) && File.Exists(_caCertPath) && File.Exists(_clientKeyPath))
      {
        // Check if ca, client and key is in the store path
        // if so use those instead.
        return;
      }

      _caCertPath = info.AuthConfig.CaCertPath;
      _clientCertPath = info.AuthConfig.ClientCertPath;
      _clientKeyPath = info.AuthConfig.ClientKeyPath;
    }
  }
}