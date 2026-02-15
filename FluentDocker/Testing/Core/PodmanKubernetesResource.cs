using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A Podman Kubernetes YAML test resource with async lifecycle.
  /// Uses <c>podman kube play</c> and <c>podman kube down</c>.
  /// </summary>
  public class PodmanKubernetesResource : ResourceBase
  {
    private readonly KubePlayConfig _config;

    /// <summary>
    /// Creates a Podman Kubernetes resource.
    /// </summary>
    /// <param name="kernel">Kernel with registered drivers.</param>
    /// <param name="config">Kubernetes play configuration.</param>
    /// <param name="options">Optional resource options.</param>
    public PodmanKubernetesResource(
        FluentDockerKernel kernel,
        KubePlayConfig config,
        DockerResourceOptions options = null)
        : base(kernel, options) => _config = config ?? throw new ArgumentNullException(nameof(config));

    /// <summary>
    /// Path to the Kubernetes YAML file.
    /// </summary>
    public string YamlPath => _config.YamlPath;

    /// <summary>
    /// The play result, available after initialization.
    /// </summary>
    public KubePlayResult PlayResult { get; private set; }

    /// <summary>
    /// All pod IDs created by the play operation.
    /// </summary>
    public IReadOnlyList<KubePlayPodResult> Pods =>
        (IReadOnlyList<KubePlayPodResult>)PlayResult?.Pods ?? Array.Empty<KubePlayPodResult>();

    /// <summary>
    /// Generates Kubernetes YAML from a running resource.
    /// </summary>
    public async Task<string> GenerateYamlAsync(
        string resourceName, CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      var driver = Kernel.SysCtl<IPodmanKubernetesDriver>(DriverId);
      var result = await driver.GenerateAsync(
          new DriverContext(DriverId), resourceName, cancellationToken);
      return result.Data;
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      var caps = await CapabilityChecks.GetCapabilitiesAsync(Kernel, DriverId, cancellationToken);
      if (!caps.SupportsKubernetes)
      {
        throw new CapabilityNotSupportedException(DriverId, "Kubernetes");
      }

      if (!Kernel.TrySysCtl<IPodmanKubernetesDriver>(DriverId, out _))
      {
        throw new InterfaceNotSupportedException(DriverId, nameof(IPodmanKubernetesDriver));
      }
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var driver = Kernel.SysCtl<IPodmanKubernetesDriver>(DriverId);
      var context = new DriverContext(DriverId);

      var result = await driver.PlayAsync(context, _config, cancellationToken);
      if (!result.Success)
      {
        throw new FluentDockerException(
            $"Podman kube play failed for '{_config.YamlPath}': {result.Error}");
      }

      PlayResult = result.Data;
      ResourceName = _config.YamlPath;
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync()
    {
      var driver = Kernel.SysCtl<IPodmanKubernetesDriver>(DriverId);
      var context = new DriverContext(DriverId);
      await driver.DownAsync(context, _config.YamlPath);
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
            "PodmanKubernetes resource is not initialized. Call InitializeAsync first.");
    }
  }
}
