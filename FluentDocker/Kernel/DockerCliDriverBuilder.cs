using System;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Internal builder for configuring the Docker CLI driver.
  /// </summary>
  internal sealed class DockerCliDriverBuilder(string driverId) : IDockerCliDriverBuilder
  {
    private readonly string _driverId = driverId;
    private string _host;
    private string _certificatePath;
    private bool _isDefault;
    private SudoMechanism _sudo = SudoMechanism.None;
    private string _sudoPassword;
    private string _binaryName;
    private string[] _searchPaths;
    private ModelRunnerEndpoint _modelEndpoint;
    private TimeSpan? _requestTimeout;

    public IDockerCliDriverBuilder AtHost(string host)
    {
      _host = host;
      return this;
    }

    public IDockerCliDriverBuilder WithRequestTimeout(TimeSpan timeout)
    {
      _requestTimeout = timeout;
      return this;
    }

    public IDockerCliDriverBuilder WithCertificates(string certificatePath)
    {
      _certificatePath = certificatePath;
      return this;
    }

    public IDockerCliDriverBuilder AsDefault()
    {
      _isDefault = true;
      return this;
    }

    public IDockerCliDriverBuilder WithSudo(SudoMechanism mechanism, string password = null)
    {
      _sudo = mechanism;
      _sudoPassword = password;
      return this;
    }

    public IDockerCliDriverBuilder WithBinary(string binaryName, params string[] searchPaths)
    {
      _binaryName = binaryName;
      _searchPaths = searchPaths is { Length: > 0 } ? searchPaths : null;
      return this;
    }

    public IDockerCliDriverBuilder WithModelRunnerEndpoint(ModelRunnerEndpoint endpoint)
    {
      _modelEndpoint = endpoint;
      return this;
    }

    internal KernelBuilder.DriverConfiguration Build()
    {
      var context = new DriverContext(_driverId)
      {
        Host = _host,
        CertificatePath = _certificatePath,
        Sudo = _sudo,
        SudoPassword = _sudoPassword,
        BinaryName = _binaryName,
        SearchPaths = _searchPaths,
        ModelRunnerEndpoint = _modelEndpoint,
        RequestTimeout = _requestTimeout,
      };

      return new KernelBuilder.DriverConfiguration
      {
        DriverId = _driverId,
        DriverPack = new DockerCliDriverPack(),
        Context = context,
        IsDefault = _isDefault,
      };
    }
  }
}
