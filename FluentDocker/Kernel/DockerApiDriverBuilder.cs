using System;
using System.Globalization;
using FluentDocker.Drivers;
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
    private const string StreamIdleTimeoutMetadataKey = "DockerApi.StreamIdleTimeoutTicks";
    private string _host;
    private string _certificatePath;
    private bool _isDefault;
    private TimeSpan? _connectionTimeout;
    private TimeSpan? _requestTimeout;
    private TimeSpan? _streamIdleTimeout;
    private string _apiVersion;
    private bool _verifyTls = true;
    private bool _allowTlsHostnameMismatch;

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
      if (timeout <= TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Connection timeout must be positive.");
      _connectionTimeout = timeout;
      return this;
    }

    public IDockerApiDriverBuilder WithRequestTimeout(TimeSpan timeout)
    {
      if (timeout <= TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Request timeout must be positive.");
      _requestTimeout = timeout;
      return this;
    }

    public IDockerApiDriverBuilder WithStreamIdleTimeout(TimeSpan timeout)
    {
      if (timeout <= TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Stream idle timeout must be positive.");
      _streamIdleTimeout = timeout;
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

    public IDockerApiDriverBuilder WithAllowTlsHostnameMismatch(bool allow = true)
    {
      _allowTlsHostnameMismatch = allow;
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
      if (_allowTlsHostnameMismatch)
      {
        context.Metadata ??= [];
        context.Metadata[DockerApiDriverMetadataKeys.AllowTlsHostnameMismatch] = "true";
      }
      if (_streamIdleTimeout.HasValue)
      {
        context.Metadata ??= [];
        context.Metadata[StreamIdleTimeoutMetadataKey] =
            _streamIdleTimeout.Value.Ticks.ToString(CultureInfo.InvariantCulture);
      }

      return new KernelBuilder.DriverConfiguration
      {
        DriverId = _driverId,
        DriverPackFactory = static () => new DockerApiDriverPack(),
        Context = context,
        IsDefault = _isDefault,
      };
    }
  }
}
