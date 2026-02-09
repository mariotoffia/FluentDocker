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
    internal class PodBuilder : IPodBuilder, IDriverScopedBuilder
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;

        FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;
        string IDriverScopedBuilder.DriverId => _driverId;

        private string _name;
        private string _hostname;
        private string _network;
        private bool _removeOnDispose;
        private readonly List<string> _ports = new();
        private readonly Dictionary<string, string> _labels = new();

        public PodBuilder(FluentDockerKernel kernel, string driverId)
        {
            _kernel = kernel;
            _driverId = driverId;
        }

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

        public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
        {
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

            var response = await driver.CreatePodAsync(context, config, cancellationToken);
            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to create pod '{_name}': {response.Error}",
                    response.ErrorCode, response.ErrorContext);
            }

            return new Services.Impl.PodService(
                _kernel, _driverId, response.Data.Id, _name, _removeOnDispose);
        }
    }
}
