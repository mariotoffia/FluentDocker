using System;
using System.Net;
using FluentDocker.Common;
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

      // Validate the env-var names exactly as the Compose models: path does — these are
      // injected verbatim into the container environment, so they must be strict identifiers.
      ModelEnvName.Validate(endpointVar, nameof(endpointVar));
      ModelEnvName.Validate(modelVar, nameof(modelVar));

      // Default to the container-internal DNS base (…/engines/v1), NOT host TCP.
      endpoint ??= ModelRunnerEndpoint.ContainerInternal().WithEngineInPath(false);

      var host = endpoint.BaseAddress.Host;
      if (IsLoopback(host))
        throw new ArgumentException(
            $"A container cannot reach the model runner at a loopback address ('{host}' resolves to the container itself). " +
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

    /// <summary>
    /// True for any loopback host — the literal <c>localhost</c> or any IPv4/IPv6
    /// loopback address (127.0.0.0/8, ::1), including the bracketed IPv6 form
    /// <c>[::1]</c> returned by <see cref="Uri.Host"/>.
    /// </summary>
    private static bool IsLoopback(string host)
    {
      if (string.IsNullOrEmpty(host))
        return false;
      if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        return true;

      var literal = host.Length > 1 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;
      return IPAddress.TryParse(literal, out var ip) && IPAddress.IsLoopback(ip);
    }
  }
}
