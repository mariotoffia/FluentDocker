using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Models;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Driver-scoped fluent entry points for the model runner, mirroring the
  /// <c>TryDriver</c>/<c>RequireDriver</c> pattern. Available on any
  /// <see cref="IDriverScopedBuilder"/> (container/network/volume/runner builders).
  /// </summary>
  public static class ModelDriverScopedBuilderExtensions
  {
    /// <summary>
    /// Begins building an <see cref="Services.IModelRunner"/> for the current driver.
    /// </summary>
    /// <param name="builder">The driver-scoped builder.</param>
    /// <returns>A runner builder.</returns>
    /// <exception cref="InterfaceNotSupportedException">The driver does not support model running.</exception>
    public static IModelRunnerBuilder UseModelRunner(this IDriverScopedBuilder builder)
    {
      if (!HasModelSupport(builder))
        throw new InterfaceNotSupportedException(builder.DriverId, nameof(IModelRunnerBuilder));

      return new ModelRunnerBuilder(builder.Kernel, builder.DriverId);
    }

    /// <summary>
    /// Attempts to begin building an <see cref="Services.IModelRunner"/>; returns
    /// false (and a null builder) when the driver lacks model support (e.g. a
    /// Podman pack without RamaLama).
    /// </summary>
    /// <param name="builder">The driver-scoped builder.</param>
    /// <param name="runnerBuilder">The runner builder, or null.</param>
    /// <returns><c>true</c> when model running is supported.</returns>
    public static bool TryUseModelRunner(this IDriverScopedBuilder builder, out IModelRunnerBuilder runnerBuilder)
    {
      if (HasModelSupport(builder))
      {
        runnerBuilder = new ModelRunnerBuilder(builder.Kernel, builder.DriverId);
        return true;
      }

      runnerBuilder = null;
      return false;
    }

    /// <summary>
    /// Begins building a managed single-model <see cref="Services.IModelService"/>.
    /// </summary>
    /// <param name="builder">The driver-scoped builder.</param>
    /// <param name="reference">The model reference.</param>
    /// <returns>A model service builder.</returns>
    public static IModelServiceBuilder UseModel(this IDriverScopedBuilder builder, string reference) =>
        new ModelServiceBuilder(builder.Kernel, builder.DriverId).ForModel(ModelReference.Parse(reference));

    private static bool HasModelSupport(IDriverScopedBuilder builder) =>
        builder.TryDriver<IModelManagementDriver>() != null
        || builder.TryDriver<IModelRuntimeDriver>() != null
        || builder.TryDriver<IModelInferenceDriver>() != null;
  }
}
