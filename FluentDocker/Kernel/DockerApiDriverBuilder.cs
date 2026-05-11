using System;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Internal builder for configuring the Docker API driver.
  /// </summary>
  internal sealed class DockerApiDriverBuilder(string driverId) : IDockerApiDriverBuilder
  {
    private readonly string _driverId = driverId;
    private string _host;
    private string _certificatePath;
    private bool _isDefault;
    private TimeSpan? _connectionTimeout;
    private TimeSpan? _requestTimeout;
    private string _apiVersion;
    private bool _verifyTls = true;

    public IDockerApiDriverBuilder AtHost(string host)
    {
      _host = host;
      return this;
    }

    public IDockerApiDriverBuilder WithCertificates(string certificatePath)
    {
      _certificatePath = certificatePath;
      return this;
    }

    public IDockerApiDriverBuilder AsDefault()
    {
      _isDefault = true;
      return this;
    }

    public IDockerApiDriverBuilder WithConnectionTimeout(TimeSpan timeout)
    {
      _connectionTimeout = timeout;
      return this;
    }

    public IDockerApiDriverBuilder WithRequestTimeout(TimeSpan timeout)
    {
      _requestTimeout = timeout;
      return this;
    }

    public IDockerApiDriverBuilder WithApiVersion(string version)
    {
      _apiVersion = version;
      return this;
    }

    public IDockerApiDriverBuilder WithTlsVerification(bool verify = true)
    {
      _verifyTls = verify;
      return this;
    }

    internal KernelBuilder.DriverConfiguration Build()
    {
      var context = new DriverContext(_driverId)
      {
        Host = _host,
        CertificatePath = _certificatePath,
        VerifyTls = _verifyTls,
        ConnectionTimeout = _connectionTimeout,
        RequestTimeout = _requestTimeout,
        ApiVersion = _apiVersion,
      };

      return new KernelBuilder.DriverConfiguration
      {
        DriverId = _driverId,
        DriverPack = new DockerApiDriverPack(),
        Context = context,
        IsDefault = _isDefault,
      };
    }
  }
}
