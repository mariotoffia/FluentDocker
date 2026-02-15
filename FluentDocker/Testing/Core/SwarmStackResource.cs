using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A Docker Swarm stack test resource with async lifecycle.
  /// Deploys and tears down a stack using the <see cref="IStackDriver"/>.
  /// </summary>
  public class SwarmStackResource : ResourceBase
  {
    private readonly StackDeployConfig _config;

    /// <summary>
    /// Creates a swarm stack resource.
    /// </summary>
    /// <param name="kernel">Kernel with registered drivers.</param>
    /// <param name="config">Stack deploy configuration.</param>
    /// <param name="options">Optional resource options.</param>
    public SwarmStackResource(
        FluentDockerKernel kernel,
        StackDeployConfig config,
        DockerResourceOptions options = null)
        : base(kernel, options) => _config = config ?? throw new ArgumentNullException(nameof(config));

    /// <summary>
    /// The stack name used for deployment.
    /// </summary>
    public string StackName => _config.StackName;

    /// <summary>
    /// The deployment result, available after initialization.
    /// </summary>
    public StackDeployResult DeployResult { get; private set; }

    /// <summary>
    /// Lists services in the deployed stack.
    /// </summary>
    public async Task<IList<StackServiceInfo>> ListServicesAsync(
        CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      var driver = Kernel.SysCtl<IStackDriver>(DriverId);
      var result = await driver.GetServicesAsync(
          new DriverContext(DriverId), StackName, cancellationToken: cancellationToken);
      return result.Data;
    }

    /// <summary>
    /// Lists tasks in the deployed stack.
    /// </summary>
    public async Task<IList<StackTask>> ListTasksAsync(
        CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      var driver = Kernel.SysCtl<IStackDriver>(DriverId);
      var result = await driver.GetTasksAsync(
          new DriverContext(DriverId), StackName, cancellationToken: cancellationToken);
      return result.Data;
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      // Ensure the driver supports stack operations.
      var caps = await CapabilityChecks.GetCapabilitiesAsync(Kernel, DriverId, cancellationToken);
      if (!caps.SupportsCompose)
      {
        throw new CapabilityNotSupportedException(DriverId, "Stack/Compose");
      }

      // Verify IStackDriver is available.
      if (!Kernel.TrySysCtl<IStackDriver>(DriverId, out _))
      {
        throw new InterfaceNotSupportedException(DriverId, nameof(IStackDriver));
      }
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var driver = Kernel.SysCtl<IStackDriver>(DriverId);
      var context = new DriverContext(DriverId);

      var result = await driver.DeployAsync(context, _config, cancellationToken);
      if (!result.Success)
      {
        throw new FluentDockerException(
            $"Stack deploy failed for '{_config.StackName}': {result.Error}");
      }

      DeployResult = result.Data;
      ResourceName = _config.StackName;
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync()
    {
      var driver = Kernel.SysCtl<IStackDriver>(DriverId);
      var context = new DriverContext(DriverId);
      await driver.RemoveAsync(context, new[] { _config.StackName });
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync()
    {
      try
      {
        await TeardownAsync();
      }
      catch
      {
        // best effort
      }
    }

    #endregion

    private void EnsureInitialized()
    {
      if (!IsInitialized)
        throw new InvalidOperationException(
            "SwarmStack resource is not initialized. Call InitializeAsync first.");
    }
  }
}
