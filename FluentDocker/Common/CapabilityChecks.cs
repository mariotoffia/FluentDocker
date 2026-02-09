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
      }
    }
  }

  /// <summary>
  /// Enum representing driver capabilities.
  /// </summary>
  public enum DriverCapability
  {
    Container,
    Network,
    Volume,
    Compose,
    Image,
    Pod,
    System
  }
}
