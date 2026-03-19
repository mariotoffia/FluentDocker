using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Helper methods for checking driver capabilities before performing operations.
  /// </summary>
  public static class CapabilityChecks
  {
    /// <summary>
    /// Ensures the driver supports container operations.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when containers are not supported.</exception>
    public static async Task EnsureContainerSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsContainers)
      {
        throw new CapabilityNotSupportedException(driverId, "Containers");
      }
    }

    /// <summary>
    /// Ensures the driver supports network operations.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when networks are not supported.</exception>
    public static async Task EnsureNetworkSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsNetworks)
      {
        throw new CapabilityNotSupportedException(driverId, "Networks");
      }
    }

    /// <summary>
    /// Ensures the driver supports volume operations.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when volumes are not supported.</exception>
    public static async Task EnsureVolumeSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsVolumes)
      {
        throw new CapabilityNotSupportedException(driverId, "Volumes");
      }
    }

    /// <summary>
    /// Ensures the driver supports compose operations.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when compose is not supported.</exception>
    public static async Task EnsureComposeSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsCompose)
      {
        throw new CapabilityNotSupportedException(driverId, "Compose");
      }
    }

    /// <summary>
    /// Ensures the driver supports image operations.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when images are not supported.</exception>
    public static async Task EnsureImageSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsImages)
      {
        throw new CapabilityNotSupportedException(driverId, "Images");
      }
    }

    /// <summary>
    /// Ensures the driver supports pod operations.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when pods are not supported.</exception>
    public static async Task EnsurePodSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsPods)
      {
        throw new CapabilityNotSupportedException(driverId, "Pods");
      }
    }

    /// <summary>
    /// Ensures the driver supports system operations.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when system operations are not supported.</exception>
    public static async Task EnsureSystemSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsSystem)
      {
        throw new CapabilityNotSupportedException(driverId, "System");
      }
    }

    /// <summary>
    /// Ensures the driver supports Kubernetes YAML operations.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when Kubernetes operations are not supported.</exception>
    public static async Task EnsureKubernetesSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsKubernetes)
      {
        throw new CapabilityNotSupportedException(driverId, "Kubernetes");
      }
    }

    /// <summary>
    /// Ensures the driver supports Swarm stack operations.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when stack operations are not supported.</exception>
    public static async Task EnsureStackSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsStacks)
      {
        throw new CapabilityNotSupportedException(driverId, "Stacks");
      }
    }

    /// <summary>
    /// Ensures the driver supports Swarm service operations.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when service operations are not supported.</exception>
    public static async Task EnsureServiceSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsServices)
      {
        throw new CapabilityNotSupportedException(driverId, "Services");
      }
    }

    /// <summary>
    /// Ensures the driver supports machine management.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when machine operations are not supported.</exception>
    public static async Task EnsureMachineSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsMachines)
      {
        throw new CapabilityNotSupportedException(driverId, "Machines");
      }
    }

    /// <summary>
    /// Ensures the driver supports manifest operations.
    /// </summary>
    /// <param name="kernel">The kernel to check.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CapabilityNotSupportedException">Thrown when manifest operations are not supported.</exception>
    public static async Task EnsureManifestSupportAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      var capabilities = await pack.GetCapabilitiesAsync(cancellationToken);

      if (!capabilities.SupportsManifests)
      {
        throw new CapabilityNotSupportedException(driverId, "Manifests");
      }
    }

    /// <summary>
    /// Gets the capabilities for a driver.
    /// </summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The driver capabilities.</returns>
    public static async Task<DriverCapabilities> GetCapabilitiesAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      return await pack.GetCapabilitiesAsync(cancellationToken);
    }

    /// <summary>
    /// Checks if the driver is healthy.
    /// </summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the driver is healthy, false otherwise.</returns>
    public static async Task<bool> IsHealthyAsync(
        FluentDockerKernel kernel,
        string driverId,
        CancellationToken cancellationToken = default)
    {
      var pack = kernel.GetDriverPack(driverId);
      return await pack.IsHealthyAsync(cancellationToken);
    }
  }

  /// <summary>
  /// Extension methods for FluentDockerKernel for capability checking.
  /// </summary>
  public static class KernelCapabilityExtensions
  {

    /// <summary>
    /// Ensures the specified capability is supported before an operation.
    /// </summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="driverId">The driver ID.</param>
    /// <param name="capability">The capability to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task EnsureCapabilityAsync(
        this FluentDockerKernel kernel,
        string driverId,
        DriverCapability capability,
        CancellationToken cancellationToken = default)
    {
      switch (capability)
      {
        case DriverCapability.Container:
          await CapabilityChecks.EnsureContainerSupportAsync(kernel, driverId, cancellationToken);
          break;
        case DriverCapability.Network:
          await CapabilityChecks.EnsureNetworkSupportAsync(kernel, driverId, cancellationToken);
          break;
        case DriverCapability.Volume:
          await CapabilityChecks.EnsureVolumeSupportAsync(kernel, driverId, cancellationToken);
          break;
        case DriverCapability.Compose:
          await CapabilityChecks.EnsureComposeSupportAsync(kernel, driverId, cancellationToken);
          break;
        case DriverCapability.Image:
          await CapabilityChecks.EnsureImageSupportAsync(kernel, driverId, cancellationToken);
          break;
        case DriverCapability.Pod:
          await CapabilityChecks.EnsurePodSupportAsync(kernel, driverId, cancellationToken);
          break;
        case DriverCapability.System:
          await CapabilityChecks.EnsureSystemSupportAsync(kernel, driverId, cancellationToken);
          break;
        case DriverCapability.Kubernetes:
          await CapabilityChecks.EnsureKubernetesSupportAsync(kernel, driverId, cancellationToken);
          break;
        case DriverCapability.Stack:
          await CapabilityChecks.EnsureStackSupportAsync(kernel, driverId, cancellationToken);
          break;
        case DriverCapability.Service:
          await CapabilityChecks.EnsureServiceSupportAsync(kernel, driverId, cancellationToken);
          break;
        case DriverCapability.Machine:
          await CapabilityChecks.EnsureMachineSupportAsync(kernel, driverId, cancellationToken);
          break;
        case DriverCapability.Manifest:
          await CapabilityChecks.EnsureManifestSupportAsync(kernel, driverId, cancellationToken);
          break;
      }
    }
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
    Manifest
  }
}
