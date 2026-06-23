using System;
using FluentDocker.Model.Models;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Wires a model endpoint into a container — the imperative counterpart to the
  /// Compose <c>models:</c> binding. It injects the endpoint as environment
  /// (<c>LLM_URL</c>/<c>LLM_MODEL</c>) and ensures reachability; it does NOT give the
  /// model a network or a volume (a model has neither — it is reached at a fixed
  /// endpoint).
  /// </summary>
  public static class ContainerModelBuilderExtensions
  {
    private const string InternalDns = "model-runner.docker.internal";

    /// <summary>
    /// Injects a model endpoint into the container's environment and ensures the
    /// container can reach it.
    /// </summary>
    /// <param name="builder">The container builder.</param>
    /// <param name="model">The model reference.</param>
    /// <param name="endpoint">
    /// The endpoint; defaults to the container-internal DNS
    /// (<c>model-runner.docker.internal:12434/engines/v1</c>). <c>localhost</c> is
    /// rejected — it would resolve to the container itself.
    /// </param>
    /// <param name="endpointVar">The env var to receive the endpoint URL (default <c>LLM_URL</c>).</param>
    /// <param name="modelVar">The env var to receive the model id (default <c>LLM_MODEL</c>).</param>
    /// <returns>The container builder for chaining.</returns>
    public static IContainerBuilder WithModel(
        this IContainerBuilder builder,
        ModelReference model,
        ModelRunnerEndpoint endpoint = null,
        string endpointVar = "LLM_URL",
        string modelVar = "LLM_MODEL")
    {
      ArgumentNullException.ThrowIfNull(builder);
      ArgumentNullException.ThrowIfNull(model);

      // Default to the container-internal DNS base (…/engines/v1), NOT host TCP.
      endpoint ??= ModelRunnerEndpoint.ContainerInternal().WithEngineInPath(false);

      var host = endpoint.BaseAddress.Host;
      if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1")
        throw new ArgumentException(
            "A container cannot reach the model runner at 'localhost' (that resolves to the container itself). " +
            "Use the container-internal DNS name (model-runner.docker.internal) or the bridge gateway (e.g. 172.17.0.1).",
            nameof(endpoint));

      var url = endpoint.ResolveUri("").ToString().TrimEnd('/');
      builder.WithEnvironment(endpointVar, url);
      builder.WithEnvironment(modelVar, model.ToString());

      // On Docker Engine the internal DNS name does not resolve unless a host-gateway
      // alias is added; on Desktop it resolves automatically (the entry is harmless there).
      if (string.Equals(host, InternalDns, StringComparison.OrdinalIgnoreCase))
        builder.WithExtraHost(InternalDns, "host-gateway");

      return builder;
    }
  }
}
