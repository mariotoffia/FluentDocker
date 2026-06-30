using System;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Internal builder for configuring the Podman CLI driver.
  /// </summary>
  internal sealed class PodmanCliDriverBuilder(string driverId) : IPodmanCliDriverBuilder
  {
    private readonly string _driverId = driverId;
    private string _host;
    private string _certificatePath;
    private bool _isDefault;
    private AutoStartMachineConfig _autoStartMachine;
    private SudoMechanism _sudo = SudoMechanism.None;
    private string _sudoPassword;
    private string _binaryName;
    private string[] _searchPaths;
    private TimeSpan? _requestTimeout;

    public IPodmanCliDriverBuilder AtHost(string host)
    {
      _host = host;
      return this;
    }

    public IPodmanCliDriverBuilder WithRequestTimeout(TimeSpan timeout)
    {
      _requestTimeout = timeout;
      return this;
    }

    public IPodmanCliDriverBuilder WithBinary(string binaryName, params string[] searchPaths)
    {
      _binaryName = binaryName;
      _searchPaths = searchPaths is { Length: > 0 } ? searchPaths : null;
      return this;
    }

    public IPodmanCliDriverBuilder WithCertificates(string certificatePath)
    {
      _certificatePath = certificatePath;
      return this;
    }

    public IPodmanCliDriverBuilder AsDefault()
    {
      _isDefault = true;
      return this;
    }

    public IPodmanCliDriverBuilder WithAutoStartMachine(
        Action<AutoStartMachineConfig> configure = null)
    {
      _autoStartMachine = new AutoStartMachineConfig();
      configure?.Invoke(_autoStartMachine);
      return this;
    }

    public IPodmanCliDriverBuilder WithSudo(SudoMechanism mechanism, string password = null)
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
        AutoStartMachine = _autoStartMachine,
        BinaryName = _binaryName,
        SearchPaths = _searchPaths,
        RequestTimeout = _requestTimeout,
      };

      return new KernelBuilder.DriverConfiguration
      {
        DriverId = _driverId,
        DriverPack = new PodmanCliDriverPack(),
        Context = context,
        IsDefault = _isDefault,
      };
    }
  }
}
