using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A Docker/Podman network test resource with async lifecycle.
  /// Creates a network on initialization and removes it on disposal.
  /// </summary>
  public class NetworkResource : ResourceBase
  {
    private readonly Action<NetworkCreateConfig> _configure;

    /// <summary>
    /// Creates a network resource.
    /// </summary>
    /// <param name="kernel">Kernel with registered drivers.</param>
    /// <param name="configure">Network configuration callback.</param>
    /// <param name="options">Optional resource options.</param>
    public NetworkResource(
        FluentDockerKernel kernel,
        Action<NetworkCreateConfig> configure,
        DockerResourceOptions options = null)
        : base(kernel, options)
    {
      ArgumentNullException.ThrowIfNull(configure);
      _configure = configure;
    }

    /// <summary>
    /// The network ID, available after initialization.
    /// </summary>
    public string NetworkId { get; private set; }

    /// <summary>
    /// The network name used during creation.
    /// </summary>
    public string NetworkName => ResourceName;

    /// <summary>
    /// Inspects the network.
    /// </summary>
    public async Task<Network> InspectAsync(CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      var driver = Kernel.SysCtl<INetworkDriver>(DriverId);
      var result = await driver.InspectAsync(
          new DriverContext(DriverId), NetworkId, cancellationToken).ConfigureAwait(false);
      return result.Success ? result.Data : null;
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      await CapabilityChecks.EnsureNetworkSupportAsync(Kernel, DriverId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var config = new NetworkCreateConfig();
      _configure(config);

      if (string.IsNullOrEmpty(config.Name))
        config.Name = GenerateUniqueName("net");

      if (Options.EnableSessionLabels)
      {
        foreach (var label in SessionLabel.CreateLabels(Options.SessionId))
          config.Labels[label.Key] = label.Value;
      }

      ResourceName = config.Name;

      var driver = Kernel.SysCtl<INetworkDriver>(DriverId);
      var result = await driver.CreateAsync(
          new DriverContext(DriverId), config, cancellationToken).ConfigureAwait(false);

      if (!result.Success)
        throw new FluentDockerException(
            $"Failed to create network '{config.Name}': {result.Error}");

      NetworkId = result.Data.Id;
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(NetworkId))
        return;

      var driver = Kernel.SysCtl<INetworkDriver>(DriverId);
      await driver.RemoveAsync(
          new DriverContext(DriverId), NetworkId, cancellationToken).ConfigureAwait(false);
      NetworkId = null;
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      var id = NetworkId;
      NetworkId = null;
      if (string.IsNullOrEmpty(id))
        return;

      try
      {
        var driver = Kernel.SysCtl<INetworkDriver>(DriverId);
        await driver.RemoveAsync(new DriverContext(DriverId), id, cancellationToken).ConfigureAwait(false);
      }
      catch { /* best effort */ }
    }

    #endregion

    private void EnsureInitialized()
    {
      if (!IsInitialized || string.IsNullOrEmpty(NetworkId))
        throw new InvalidOperationException(
            "Network resource is not initialized. Call InitializeAsync first.");
    }
  }
}
