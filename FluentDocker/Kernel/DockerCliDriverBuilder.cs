using System;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;

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

    public IDockerCliDriverBuilder AtHost(string host)
    {
      _host = host;
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

    internal KernelBuilder.DriverConfiguration Build()
    {
      var context = new DriverContext(_driverId)
      {
        Host = _host,
        CertificatePath = _certificatePath,
        Sudo = _sudo,
        SudoPassword = _sudoPassword,
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
