using System;
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
      ArgumentNullException.ThrowIfNull(builder);
      if (!HasAnyModelPort(builder))
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
    public static bool TryUseModelRunner(this IDriverScopedBuilder builder, out IModelRunnerBuilder? runnerBuilder)
    {
      ArgumentNullException.ThrowIfNull(builder);
      if (HasAnyModelPort(builder))
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
    /// <param name="reference">The model reference string.</param>
    /// <returns>A model service builder.</returns>
    /// <exception cref="InterfaceNotSupportedException">The driver does not support model running.</exception>
    public static IModelServiceBuilder UseModel(this IDriverScopedBuilder builder, string reference)
    {
      ArgumentNullException.ThrowIfNull(builder);
      return UseModel(builder, ModelReference.Parse(reference));
    }

    /// <summary>
    /// Begins building a managed single-model <see cref="Services.IModelService"/> from a
    /// pre-built <see cref="ModelReference"/>.
    /// </summary>
    /// <param name="builder">The driver-scoped builder.</param>
    /// <param name="reference">The model reference.</param>
    /// <returns>A model service builder.</returns>
    /// <exception cref="InterfaceNotSupportedException">The driver does not support model running.</exception>
    public static IModelServiceBuilder UseModel(this IDriverScopedBuilder builder, ModelReference reference)
    {
      ArgumentNullException.ThrowIfNull(builder);
      if (!HasModelRuntime(builder))
        throw new InterfaceNotSupportedException(builder.DriverId, nameof(IModelRuntimeDriver));

      return new ModelServiceBuilder(builder.Kernel, builder.DriverId).ForModel(reference);
    }

    /// <summary>
    /// Checks whether the driver exposes any model port.
    /// </summary>
    internal static bool HasAnyModelPort(IDriverScopedBuilder builder) =>
        builder.TryDriver<IModelManagementDriver>() != null
        || builder.TryDriver<IModelRuntimeDriver>() != null
        || builder.TryDriver<IModelInferenceDriver>() != null;

    /// <summary>
    /// Checks whether the driver exposes model runtime control.
    /// </summary>
    internal static bool HasModelRuntime(IDriverScopedBuilder builder) =>
        builder.TryDriver<IModelRuntimeDriver>() != null;
  }
}
