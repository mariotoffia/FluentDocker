using System;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Model-builder entry points on <see cref="Builder"/> (<c>UseModelRunner</c>/<c>UseModel</c>).
  /// Unlike the other <c>UseXxx</c> operations these return a builder directly and are not part of
  /// the deferred build pipeline. Split into its own partial file purely to keep each source file
  /// within the repository's 500-line limit.
  /// </summary>
  public partial class Builder
  {
    /// <summary>
    /// Begins building an <see cref="Services.IModelRunner"/> in the current scope
    /// (set by <see cref="WithinDriver(string, FluentDockerKernel)"/>). Unlike the
    /// other <c>UseXxx</c> operations this returns the runner builder directly
    /// (the runner is not part of the deferred build pipeline).
    /// </summary>
    /// <returns>A model runner builder.</returns>
    public IModelRunnerBuilder UseModelRunner()
    {
      ValidateScope();
      if (_operations.Count > 0)
        throw new InvalidOperationException(ModelBuilderAfterOpsMessage);
      var builder = new ModelRunnerBuilder(_currentKernel, _currentDriverId);
      // Shared fail-fast capability guard (same one the driver-scoped extensions use).
      if (!ModelDriverScopedBuilderExtensions.HasAnyModelPort(builder))
        throw new Common.InterfaceNotSupportedException(_currentDriverId, nameof(IModelRunnerBuilder));
      return builder;
    }

    /// <summary>
    /// Begins building a managed single-model <see cref="Services.IModelService"/> in
    /// the current scope.
    /// </summary>
    /// <param name="reference">The model reference string.</param>
    /// <returns>A model service builder.</returns>
    public IModelServiceBuilder UseModel(string reference) =>
        UseModel(Model.Models.ModelReference.Parse(reference));

    /// <summary>
    /// Begins building a managed single-model <see cref="Services.IModelService"/> in
    /// the current scope from a pre-built <see cref="Model.Models.ModelReference"/>.
    /// </summary>
    /// <param name="reference">The model reference.</param>
    /// <returns>A model service builder.</returns>
    public IModelServiceBuilder UseModel(Model.Models.ModelReference reference)
    {
      ValidateScope();
      if (_operations.Count > 0)
        throw new InvalidOperationException(ModelBuilderAfterOpsMessage);
      var serviceBuilder = new ModelServiceBuilder(_currentKernel, _currentDriverId);
      if (!ModelDriverScopedBuilderExtensions.HasModelRuntime(serviceBuilder))
        throw new Common.InterfaceNotSupportedException(_currentDriverId, nameof(Drivers.IModelRuntimeDriver));
      return serviceBuilder.ForModel(reference);
    }
  }
}
