using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Pod builder implementation. Creates a Podman pod.
  /// </summary>
  internal sealed class PodBuilder(FluentDockerKernel kernel, string driverId) : IPodBuilder, IDriverScopedBuilder
  {
    private readonly FluentDockerKernel _kernel = kernel;
    private readonly string _driverId = driverId;

    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;
    string IDriverScopedBuilder.DriverId => _driverId;

    private string _name;
    private string _hostname;
    private string _network;
    private bool _removeOnDispose;
    private readonly List<string> _ports = [];
    private readonly Dictionary<string, string> _labels = [];
    internal IServiceAsync PendingService { get; private set; }
    internal bool CreatedResource { get; private set; }

    public IPodBuilder WithName(string name) { _name = name; return this; }

    public IPodBuilder WithPort(string hostPort, string containerPort)
    {
      _ports.Add($"{hostPort}:{containerPort}");
      return this;
    }

    public IPodBuilder ExposePort(string containerPort)
    {
      _ports.Add(containerPort);
      return this;
    }

    public IPodBuilder WithNetwork(string networkName) { _network = networkName; return this; }
    public IPodBuilder WithLabel(string key, string value) { _labels[key] = value; return this; }
    public IPodBuilder WithHostname(string hostname) { _hostname = hostname; return this; }
    public IPodBuilder RemoveOnDispose() { _removeOnDispose = true; return this; }

    internal void ResetForRetry()
    {
      PendingService = null;
      CreatedResource = false;
    }

    public async Task<IServiceAsync> ExecuteAsync(CancellationToken cancellationToken)
    {
      Validate();
      var driver = _kernel.SysCtl<IPodmanPodDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new PodCreateConfig
      {
        Name = _name,
        Network = _network,
        Hostname = _hostname,
        Labels = _labels,
        Ports = _ports,
      };

      var response = await driver.CreatePodAsync(context, config, cancellationToken).ConfigureAwait(false);
      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to create pod '{_name}': {response.Error}",
            response.ErrorCode, response.ErrorContext);
      }

      CreatedResource = true;
      var service = new Services.Impl.PodService(
          _kernel, _driverId, response.Data.Id, _name, _removeOnDispose);
      PendingService = service;
      await service.StartAsync(cancellationToken).ConfigureAwait(false);
      return service;
    }

    private void Validate()
    {
      if (string.IsNullOrWhiteSpace(_name))
        throw new FluentDockerException("Pod name is required. Call WithName() before building.");
      foreach (var port in _ports)
      {
        // Format: [[ip:][hostPort]:]containerPort[/proto] — container port is the last segment.
        var colon = port.LastIndexOf(':');
        if (colon >= 0)
          ContainerBuilder.ValidateHostPort(port[..colon]);
        ValidatePort(colon >= 0 ? port[(colon + 1)..] : port);
      }
    }

    private static void ValidatePort(string port)
    {
      var slash = port.IndexOf('/');
      var portPart = slash >= 0 ? port[..slash] : port;
      if (!int.TryParse(portPart, out var value) || value < 1 || value > 65535)
        throw new FluentDockerException($"Invalid pod port '{port}'. Port must be 1-65535.");
    }
  }
}
