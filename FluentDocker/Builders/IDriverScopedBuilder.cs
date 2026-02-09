using FluentDocker.Kernel;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Interface for builders that have access to a kernel and driver context.
  /// Extension methods use this to resolve driver-specific interfaces,
  /// enabling composable, driver-aware fluent APIs.
  /// </summary>
  public interface IDriverScopedBuilder
  {
    /// <summary>
    /// The kernel instance backing this builder.
    /// </summary>
    FluentDockerKernel Kernel { get; }

    /// <summary>
    /// The driver ID scoped to this builder.
    /// </summary>
    string DriverId { get; }
  }
}
