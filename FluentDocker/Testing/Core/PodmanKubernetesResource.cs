using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A Podman Kubernetes YAML test resource with async lifecycle.
  /// Uses <c>podman kube play</c> and <c>podman kube down</c>.
  /// </summary>
  public class PodmanKubernetesResource : ResourceBase
  {
    private readonly KubePlayConfig _config;
    private static readonly Action<ILogger, Exception?> MissingSessionLabels =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(MissingSessionLabels)),
            "Podman Kubernetes resources cannot apply FluentDocker session labels or auto-scope resource names, because pod/service names live inside your YAML and are not rewritten. Parallel runs of the same manifest WILL collide; give each run unique metadata.names (or an isolated host/session) and run kube-specific cleanup for leaks.");

    /// <summary>
    /// Creates a Podman Kubernetes resource.
    /// </summary>
    /// <param name="kernel">Kernel with registered drivers.</param>
    /// <param name="config">Kubernetes play configuration.</param>
    /// <param name="options">Optional resource options.</param>
    public PodmanKubernetesResource(
        FluentDockerKernel kernel,
        KubePlayConfig config,
        DockerResourceOptions? options = null)
        : base(kernel, options)
    {
      ArgumentNullException.ThrowIfNull(config);
      _config = config;
      if (string.IsNullOrWhiteSpace(config.YamlPath))
        throw new ArgumentException("YamlPath must not be null or empty.", nameof(config));
    }

    /// <summary>
    /// Path to the Kubernetes YAML file.
    /// </summary>
    public string YamlPath => _config.YamlPath!;

    /// <summary>
    /// The play result, available after initialization.
    /// </summary>
    public KubePlayResult? PlayResult { get; private set; }

    /// <summary>
    /// All pod IDs created by the play operation.
    /// </summary>
    public IReadOnlyList<KubePlayPodResult> Pods =>
        PlayResult?.Pods?.ToList().AsReadOnly()
        ?? (IReadOnlyList<KubePlayPodResult>)[];

    /// <summary>
    /// Generates Kubernetes YAML from a running resource.
    /// </summary>
    public async Task<string> GenerateYamlAsync(
        string resourceName, CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      var driver = Kernel.SysCtl<IPodmanKubernetesDriver>(DriverId);
      var result = await driver.GenerateAsync(
          new DriverContext(DriverId), resourceName, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        throw new FluentDockerException(
            $"Failed to generate YAML for '{resourceName}': {result.Error}");
      return result.Data!;
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      var caps = await CapabilityChecks.GetCapabilitiesAsync(Kernel, DriverId, cancellationToken).ConfigureAwait(false);
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
      var generation = ProvisionGeneration;
      if (Options.EnableSessionLabels)
        MissingSessionLabels(Logger, null);

      var driver = Kernel.SysCtl<IPodmanKubernetesDriver>(DriverId);
      var context = new DriverContext(DriverId);

      var result = await driver.PlayAsync(context, _config, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
      {
        throw new FluentDockerException(
            $"Podman kube play failed for '{_config.YamlPath}': {result.Error}");
      }

      var playResult = result.Data
          ?? throw new FluentDockerException(
              $"Podman kube play for '{_config.YamlPath}' returned Success " +
              "but no result payload.");
      if (TryCommitProvision(generation, () =>
      {
        PlayResult = playResult;
        ResourceName = _config.YamlPath!;
      }))
      {
        return;
      }

      await RemoveStaleKubeAsync(driver, context, generation).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      var driver = Kernel.SysCtl<IPodmanKubernetesDriver>(DriverId);
      var context = new DriverContext(DriverId);
      var result = await driver.DownAsync(
          context, _config.YamlPath!, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        throw new FluentDockerException(
            $"Failed to tear down Podman kube for '{_config.YamlPath}': {result.Error}");
      PlayResult = null;
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      if (PlayResult == null)
        return;

      var driver = Kernel.SysCtl<IPodmanKubernetesDriver>(DriverId);
      var context = new DriverContext(DriverId);
      var result = await driver.DownAsync(
          context, _config.YamlPath!, cancellationToken).ConfigureAwait(false);

      if (result.Success || IsNotFound(result))
      {
        PlayResult = null;
        return;
      }

      throw new DriverException(
          $"Failed to force-remove Podman kube for '{_config.YamlPath}': {result.Error}",
          result.ErrorCode!,
          result.ErrorContext);
    }

    #endregion

    private static bool IsNotFound(CommandResponse<Unit> result)
    {
      return result.ErrorCode == ErrorCodes.Driver.NotFound ||
             result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ||
             result.Error?.Contains("no such", StringComparison.OrdinalIgnoreCase) == true;
    }

    private void EnsureInitialized()
    {
      if (!IsInitialized)
        throw new InvalidOperationException(
            "PodmanKubernetes resource is not initialized. Call InitializeAsync first.");
    }

    private async Task RemoveStaleKubeAsync(
        IPodmanKubernetesDriver driver,
        DriverContext context,
        int generation)
    {
      if (!ShouldCleanupRejectedProvision(generation))
        return;

      try
      {
        using var cts = new CancellationTokenSource(Options.TeardownTimeout);
        var downTask = driver.DownAsync(context, _config.YamlPath!, cts.Token);
        var result = await downTask.WaitAsync(cts.Token).ConfigureAwait(false);
        if (!result.Success && !IsNotFound(result))
          OrphanCleanup.MarkAbandonedLateProvision(_config.YamlPath!, Options.SessionId);
      }
      catch
      {
        OrphanCleanup.MarkAbandonedLateProvision(_config.YamlPath!, Options.SessionId);
      }
    }
  }
}
