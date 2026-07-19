using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Interface for the v3.0.0 fluent builder.
  /// Exposes only operations common to all drivers. For driver-specific
  /// operations, use the typed builder returned by
  /// <see cref="Builder.WithinDockerCli"/>, <see cref="Builder.WithinDockerApi"/>,
  /// or <see cref="Builder.WithinPodmanCli"/>.
  /// </summary>
  public interface IBuilder
  {
    /// <summary>
    /// Selects the driver scope used by subsequent builder operations.
    /// </summary>
    /// <param name="driverId">Driver identifier registered in the kernel.</param>
    /// <param name="kernel">Kernel instance. Required on the first scope selection.</param>
    /// <returns>The builder for fluent chaining.</returns>
    Builder WithinDriver(string driverId, FluentDockerKernel? kernel = null);

    /// <summary>
    /// Adds a container operation to the current driver scope.
    /// </summary>
    /// <param name="configure">Container configuration delegate.</param>
    /// <returns>The builder for fluent chaining.</returns>
    Builder UseContainer(Action<IContainerBuilder> configure);

    /// <summary>
    /// Adds a network operation to the current driver scope.
    /// </summary>
    /// <param name="configure">Network configuration delegate.</param>
    /// <returns>The builder for fluent chaining.</returns>
    Builder UseNetwork(Action<INetworkBuilder> configure);

    /// <summary>
    /// Adds a volume operation to the current driver scope.
    /// </summary>
    /// <param name="configure">Volume configuration delegate.</param>
    /// <returns>The builder for fluent chaining.</returns>
    Builder UseVolume(Action<IVolumeBuilder> configure);

    /// <summary>
    /// Adds an image build operation.
    /// </summary>
    Builder UseImage(string imageName, Action<DockerfileBuilder> configure);

    /// <summary>
    /// Builds all operations synchronously (TERMINAL operation).
    /// For async contexts, prefer BuildAsync() to avoid deadlocks.
    /// </summary>
    BuildResults Build();

    /// <summary>
    /// Builds all operations asynchronously (TERMINAL operation).
    /// </summary>
    /// <remarks>
    /// A builder is single-use after a successful build; retry is allowed after a failed build.
    /// Operation order is preserved within each driver scope; cross-scope operations are grouped
    /// by driver before execution.
    /// On failure, resources created by this builder are removed; pre-existing resources reused
    /// by name are borrowed and left untouched. The thrown exception carries a
    /// <see cref="BuildFailureManifest"/> in <see cref="Exception.Data"/> under
    /// <c>BuildFailureManifest</c>.
    /// </remarks>
    /// <param name="cleanupTimeout">
    /// Maximum time allowed for cleanup on build failure. Must be non-negative
    /// (<see cref="TimeSpan.Zero"/> or greater); a negative value throws
    /// <see cref="ArgumentOutOfRangeException"/>. Defaults to 120 seconds when null.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the build.</param>
    Task<BuildResults> BuildAsync(
        TimeSpan? cleanupTimeout = null,
        CancellationToken cancellationToken = default);
  }
}
