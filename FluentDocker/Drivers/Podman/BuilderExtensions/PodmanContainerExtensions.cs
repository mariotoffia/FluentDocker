using System;
using FluentDocker.Builders;

namespace FluentDocker.Drivers.Podman.BuilderExtensions
{
  /// <summary>
  /// Podman-specific extension methods for IContainerBuilder.
  /// These extensions are driver-aware and fail when the current driver
  /// does not support the requested interface.
  /// </summary>
  public static class PodmanContainerExtensions
  {
    /// <summary>
    /// Associates this container with a Podman pod.
    /// Throws if the current driver does not support pods.
    /// </summary>
    /// <param name="builder">Container builder.</param>
    /// <param name="podName">Name of the pod to join.</param>
    /// <returns>The builder for chaining.</returns>
    public static IContainerBuilder UsePod(this IContainerBuilder builder, string podName)
    {
      if (builder is not IDriverScopedBuilder scoped)
        throw new InvalidOperationException("UsePod requires a driver-scoped builder.");

      if (scoped.TryDriver<IPodmanPodDriver>() == null)
        throw new InvalidOperationException("UsePod requires a Podman driver with pod support.");

      return builder.WithPod(podName);
    }
  }
}
