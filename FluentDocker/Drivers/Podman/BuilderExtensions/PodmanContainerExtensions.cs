using FluentDocker.Builders;

namespace FluentDocker.Drivers.Podman.BuilderExtensions
{
    /// <summary>
    /// Podman-specific extension methods for IContainerBuilder.
    /// These extensions are driver-aware and gracefully no-op when the
    /// current driver does not support the requested interface.
    /// </summary>
    public static class PodmanContainerExtensions
    {
        /// <summary>
        /// Associates this container with a Podman pod.
        /// No-op if the current driver does not support pods.
        /// </summary>
        /// <param name="builder">Container builder.</param>
        /// <param name="podName">Name of the pod to join.</param>
        /// <returns>The builder for chaining.</returns>
        public static IContainerBuilder UsePod(this IContainerBuilder builder, string podName)
        {
            if (builder is IDriverScopedBuilder scoped)
            {
                var podDriver = scoped.TryDriver<IPodmanPodDriver>();
                if (podDriver != null)
                    builder.WithPod(podName);
            }
            return builder;
        }
    }
}
