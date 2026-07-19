using System;
using FluentDocker.Builders;

namespace FluentDocker.Builders.Compose
{
  /// <summary>
  /// Model-runner extensions for <see cref="IComposeBuilder"/>, kept off the interface
  /// to avoid breaking external implementers (mirrors <c>UseModelRunner</c>/<c>UseModel</c>).
  /// </summary>
  public static class ComposeBuilderModelExtensions
  {
    /// <summary>
    /// Adds a first-class Compose <c>models:</c> overlay (Docker Model Runner). The
    /// configured <see cref="IComposeModelBuilder"/> is rendered to a managed temporary
    /// overlay file that is appended to the compose-files list (so Compose merges it) and
    /// is automatically deleted when the resulting
    /// <see cref="FluentDocker.Services.IComposeService"/> is torn down / disposed. This
    /// removes the need to hand-write, pass and delete the overlay file manually.
    /// </summary>
    /// <param name="builder">The compose builder.</param>
    /// <param name="configure">Configures the <c>models:</c> map and per-service bindings.</param>
    /// <returns>The builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="NotSupportedException">The builder is not the built-in <see cref="ComposeBuilder"/>.</exception>
    public static IComposeBuilder WithModels(this IComposeBuilder builder, Action<IComposeModelBuilder> configure)
    {
      ArgumentNullException.ThrowIfNull(builder);
      if (builder is ComposeBuilder cb)
        return cb.WithModelsInternal(configure);
      throw new NotSupportedException(
          $"WithModels requires the built-in compose builder; got {builder.GetType().Name}.");
    }
  }
}
