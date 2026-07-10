using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Helper methods for checking driver capabilities before performing operations.
  /// </summary>
  /// <remarks>
  /// These checks validate the driver's declared capability surface, not live daemon/runtime
  /// feature availability. A check can pass for a feature the current daemon lacks (for example,
  /// stack support on a non-swarm daemon); use <see cref="IDriverPack.IsHealthyAsync"/> and the
  /// operation's result or exception for runtime truth.
  /// </remarks>
  public static class CapabilityChecks
  {
    /// <summary>Ensures the driver supports container operations.</summary>
    public static Task EnsureContainerSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Container, cancellationToken);

    /// <summary>Ensures the driver supports network operations.</summary>
    public static Task EnsureNetworkSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Network, cancellationToken);

    /// <summary>Ensures the driver supports volume operations.</summary>
    public static Task EnsureVolumeSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Volume, cancellationToken);

    /// <summary>Ensures the driver supports compose operations.</summary>
    public static Task EnsureComposeSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Compose, cancellationToken);

    /// <summary>Ensures the driver supports image operations.</summary>
    public static Task EnsureImageSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Image, cancellationToken);

    /// <summary>Ensures the driver supports pod operations.</summary>
    public static Task EnsurePodSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Pod, cancellationToken);

    /// <summary>Ensures the driver supports system operations.</summary>
    public static Task EnsureSystemSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.System, cancellationToken);

    /// <summary>Ensures the driver supports Kubernetes YAML operations.</summary>
    public static Task EnsureKubernetesSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Kubernetes, cancellationToken);

    /// <summary>Ensures the driver supports Swarm stack operations.</summary>
    public static Task EnsureStackSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Stack, cancellationToken);

    /// <summary>Ensures the driver supports Swarm service operations.</summary>
    public static Task EnsureServiceSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Service, cancellationToken);

    /// <summary>Ensures the driver supports machine management.</summary>
    public static Task EnsureMachineSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Machine, cancellationToken);

    /// <summary>Ensures the driver supports manifest operations.</summary>
    public static Task EnsureManifestSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Manifest, cancellationToken);

    /// <summary>Ensures the driver supports model operations.</summary>
    public static Task EnsureModelSupportAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default) =>
        EnsureCapabilitySupportAsync(sysCtl, driverId, DriverCapability.Model, cancellationToken);

    /// <summary>
    /// Gets the capabilities for a driver.
    /// </summary>
    public static Task<DriverCapabilities> GetCapabilitiesAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(sysCtl);
      return sysCtl.GetCapabilitiesAsync(driverId, cancellationToken);
    }

    /// <summary>
    /// Checks if the driver is healthy.
    /// </summary>
    public static Task<bool> IsHealthyAsync(
        ISysCtl sysCtl,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(sysCtl);
      return sysCtl.IsHealthyAsync(driverId, cancellationToken);
    }

    internal static async Task EnsureCapabilitySupportAsync(
        ISysCtl sysCtl,
        string driverId,
        DriverCapability capability,
        CancellationToken cancellationToken)
    {
      ArgumentNullException.ThrowIfNull(sysCtl);
      var descriptor = GetCapabilityDescriptor(capability);
      var capabilities = await sysCtl.GetCapabilitiesAsync(driverId, cancellationToken).ConfigureAwait(false);
      if (!descriptor.IsSupported(capabilities))
        throw new CapabilityNotSupportedException(driverId, descriptor.Name);
    }

    private static (string Name, Func<DriverCapabilities, bool> IsSupported) GetCapabilityDescriptor(
        DriverCapability capability) =>
        capability switch
        {
          DriverCapability.Container => ("Containers", c => c.SupportsContainers),
          DriverCapability.Network => ("Networks", c => c.SupportsNetworks),
          DriverCapability.Volume => ("Volumes", c => c.SupportsVolumes),
          DriverCapability.Compose => ("Compose", c => c.SupportsCompose),
          DriverCapability.Image => ("Images", c => c.SupportsImages),
          DriverCapability.Pod => ("Pods", c => c.SupportsPods),
          DriverCapability.System => ("System", c => c.SupportsSystem),
          DriverCapability.Kubernetes => ("Kubernetes", c => c.SupportsKubernetes),
          DriverCapability.Stack => ("Stacks", c => c.SupportsStacks),
          DriverCapability.Service => ("Services", c => c.SupportsServices),
          DriverCapability.Machine => ("Machines", c => c.SupportsMachines),
          DriverCapability.Manifest => ("Manifests", c => c.SupportsManifests),
          DriverCapability.Model => ("Models", c => c.SupportsModels),
          _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null)
        };

  }

  /// <summary>
  /// Extension methods for FluentDockerKernel for capability checking.
  /// </summary>
  public static class KernelCapabilityExtensions
  {
    /// <summary>
    /// Ensures the specified capability is supported before an operation.
    /// </summary>
    public static Task EnsureCapabilityAsync(
        this ISysCtl sysCtl,
        string driverId,
        DriverCapability capability,
        CancellationToken cancellationToken = default) =>
        CapabilityChecks.EnsureCapabilitySupportAsync(sysCtl, driverId, capability, cancellationToken);
  }

  /// <summary>
  /// Enum representing driver capabilities.
  /// </summary>
  public enum DriverCapability
  {
    /// <summary>Container lifecycle operations (create, start, stop, remove).</summary>
    Container,
    /// <summary>Network management operations.</summary>
    Network,
    /// <summary>Volume management operations.</summary>
    Volume,
    /// <summary>Docker Compose / Podman Compose operations.</summary>
    Compose,
    /// <summary>Image pull, build, and management operations.</summary>
    Image,
    /// <summary>Pod management operations (Podman only).</summary>
    Pod,
    /// <summary>System-level operations (info, version, ping).</summary>
    System,
    /// <summary>Kubernetes YAML play/generate operations (Podman only).</summary>
    Kubernetes,
    /// <summary>Docker Swarm stack operations.</summary>
    Stack,
    /// <summary>Docker Swarm service operations.</summary>
    Service,
    /// <summary>Machine management operations (docker-machine, podman machine).</summary>
    Machine,
    /// <summary>Multi-architecture manifest operations.</summary>
    Manifest,
    /// <summary>Docker Model Runner operations.</summary>
    Model
  }
}
