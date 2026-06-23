using System;

namespace FluentDocker.Builders.Compose
{
  /// <summary>
  /// Fluent builder for a single top-level Compose <c>models:</c> entry.
  /// </summary>
  public interface IComposeModelSpecBuilder
  {
    /// <summary>Sets the model reference (<c>ai/…</c> | <c>hf.co/…</c>).</summary>
    IComposeModelSpecBuilder WithModel(string reference);

    /// <summary>Sets the context size.</summary>
    IComposeModelSpecBuilder WithContextSize(int tokens);

    /// <summary>Sets raw engine runtime flags.</summary>
    IComposeModelSpecBuilder WithRuntimeFlags(params string[] flags);
  }

  /// <summary>
  /// Builds a Compose <c>models:</c> overlay: a top-level <c>models:</c> map and
  /// per-service <c>models:</c> bindings, rendered as a Compose file that merges
  /// with a user's compose file via an additional <c>-f</c>.
  /// </summary>
  public interface IComposeModelBuilder
  {
    /// <summary>Adds a top-level model entry.</summary>
    /// <param name="key">The model key.</param>
    /// <param name="spec">Configures the model entry.</param>
    /// <returns>This builder.</returns>
    IComposeModelBuilder AddModel(string key, Action<IComposeModelSpecBuilder> spec);

    /// <summary>Binds a model to a service. When both var names are null the short list form is used.</summary>
    /// <param name="service">The service name.</param>
    /// <param name="modelKey">The model key.</param>
    /// <param name="endpointVar">The custom endpoint env-var name (long form), or null.</param>
    /// <param name="modelVar">The custom model-id env-var name (long form), or null.</param>
    /// <returns>This builder.</returns>
    IComposeModelBuilder BindToService(string service, string modelKey, string endpointVar = null, string modelVar = null);
  }
}
