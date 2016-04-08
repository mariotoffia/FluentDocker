using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerHostService : ServiceBase, IHostService
  {
    private const string DockerHost = "DOCKER_HOST";
    private const string DockerCertPath = "DOCKER_CERT_PATH";
    private const string DockerTlsVerify = "DOCKER_TLS_VERIFY";
    private const string DefaultCaCertName = "ca.pem";
    private const string DefaultClientCertName = "cert.pem";
    private const string DefaultClientKeyName = "key.pem";
    private readonly string _caCertPath;
    private readonly string _clientCertPath;
    private readonly string _clientKeyPath;

    private readonly bool _stopWhenDisposed;

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
      Host = name.Uri();

      var info = name.Inspect().Data;
      RequireTls = info.RequireTls;

      ClientCaCertificate =
        Path.GetDirectoryName(info.AuthConfig.CaCertPath).ToCertificate(Path.GetFileName(info.AuthConfig.CaCertPath));

      ClientCaCertificate =
        Path.GetDirectoryName(info.AuthConfig.ClientCertPath)
          .ToCertificate(Path.GetFileName(info.AuthConfig.ClientCertPath),
            Path.GetFileName(info.AuthConfig.ClientKeyPath));

      State = name.Status();

      _caCertPath = info.AuthConfig.CaCertPath;
      _clientCertPath = info.AuthConfig.ClientCertPath;
      _clientKeyPath = info.AuthConfig.ClientKeyPath;
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
      if (State != ServiceRunningState.Stopped)
      {
        throw new InvalidOperationException($"Cannot start docker host {Name} since it has state {State}");
      }

      var response = Name.Start();
      if (!response.Success)
      {
        throw new InvalidOperationException($"Could not start docker host {Name}");
      }
    }

    public Uri Host { get; }
    public bool IsNative { get; }
    public bool RequireTls { get; }
    public X509Certificate2 ClientCertificate { get; }
    public X509Certificate2 ClientCaCertificate { get; }

    public IList<IContainerService> RunningContainers => GetContainers(false);

    public IList<IContainerService> GetContainers(bool all = true, string filter = null)
    {
      var options = "--quiet";
      if (all)
      {
        options += " --all";
      }

      if (string.IsNullOrEmpty(filter))
      {
        options += $"--filter=[{filter}]";
      }

      var result = Host.Ps(options, _caCertPath, _clientCertPath, _clientKeyPath);
      if (!result.Success)
      {
        return new List<IContainerService>();
      }

      var config = Host.InspectContainer(result.Log[0], _clientCertPath, _clientCertPath, _clientKeyPath);
      // TODO: Create ContainerService from the config retrieved.

      throw new NotImplementedException();
    }
  }
}