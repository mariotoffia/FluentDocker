using System;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Services.Impl;

namespace FluentDocker.Services
{
  /// <summary>
  /// Reconstructs an <see cref="IModelRunner"/> from the environment variables that
  /// Docker Model Runner injects into a model-bound workload (notably via Compose):
  /// <c>&lt;PREFIX&gt;_URL</c> and <c>&lt;PREFIX&gt;_MODEL</c> (default prefix <c>LLM</c>).
  /// This closes the loop: code running inside a model-bound container can drive the
  /// model through the same interface as everything else.
  /// </summary>
  public static class ModelRunnerEnvironment
  {
    private const string DefaultPrefix = "LLM";

    /// <summary>
    /// Builds a runner from the injected environment variables.
    /// </summary>
    /// <param name="prefix">The env-var prefix (default <c>LLM</c> → <c>LLM_URL</c>/<c>LLM_MODEL</c>).</param>
    /// <param name="apiKey">Optional bearer token for the endpoint.</param>
    /// <returns>An inference-capable <see cref="IModelRunner"/>.</returns>
    /// <exception cref="InvalidOperationException">The required URL variable is not set or invalid.</exception>
    public static IModelRunner FromEnvironment(string prefix = null, string apiKey = null)
    {
      if (!TryFromEnvironment(out var runner, prefix, apiKey))
      {
        var p = Normalize(prefix);
        throw new InvalidOperationException(
            $"Model endpoint environment variable '{p}_URL' is not set or is not a valid absolute URI. " +
            "This factory expects the variables injected by a Docker Model Runner / Compose 'models:' binding.");
      }

      return runner;
    }

    /// <summary>
    /// Attempts to build a runner from the injected environment variables.
    /// </summary>
    /// <param name="runner">The resulting runner, or <c>null</c>.</param>
    /// <param name="prefix">The env-var prefix (default <c>LLM</c>).</param>
    /// <param name="apiKey">Optional bearer token.</param>
    /// <returns><c>true</c> when <c>&lt;PREFIX&gt;_URL</c> is set to a valid absolute URI.</returns>
    public static bool TryFromEnvironment(out IModelRunner runner, string prefix = null, string apiKey = null)
    {
      runner = null;
      var p = Normalize(prefix);

      var url = Environment.GetEnvironmentVariable($"{p}_URL");
      if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return false;

      var modelValue = Environment.GetEnvironmentVariable($"{p}_MODEL");
      runner = CreateRunner(ModelRunnerEndpoint.Raw(uri), modelValue, apiKey);
      return true;
    }

    /// <summary>
    /// Builds a runner from explicitly-named environment variables (the Compose
    /// long-form binding's <c>endpoint_var</c> / <c>model_var</c>).
    /// </summary>
    /// <param name="endpointVar">The env var holding the endpoint URL.</param>
    /// <param name="modelVar">The env var holding the model id.</param>
    /// <param name="apiKey">Optional bearer token.</param>
    /// <returns>An inference-capable <see cref="IModelRunner"/>.</returns>
    /// <exception cref="InvalidOperationException">The endpoint variable is unset or invalid.</exception>
    public static IModelRunner FromVariables(string endpointVar, string modelVar, string apiKey = null)
    {
      if (!TryFromVariables(endpointVar, modelVar, out var runner, apiKey))
        throw new InvalidOperationException($"Model endpoint environment variable '{endpointVar}' is not set or is not a valid absolute URI.");

      return runner;
    }

    /// <summary>
    /// Attempts to build a runner from explicitly-named environment variables.
    /// </summary>
    /// <param name="endpointVar">The env var holding the endpoint URL.</param>
    /// <param name="modelVar">The env var holding the model id.</param>
    /// <param name="runner">The resulting runner, or null.</param>
    /// <param name="apiKey">Optional bearer token.</param>
    /// <returns><c>true</c> when the endpoint variable is set to a valid absolute URI.</returns>
    public static bool TryFromVariables(string endpointVar, string modelVar, out IModelRunner runner, string apiKey = null)
    {
      runner = null;

      var url = Environment.GetEnvironmentVariable(endpointVar);
      if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return false;

      runner = CreateRunner(ModelRunnerEndpoint.Raw(uri), Environment.GetEnvironmentVariable(modelVar), apiKey);
      return true;
    }

    private static string Normalize(string prefix) => string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix;

    /// <summary>
    /// Composes a generic OpenAI runner (HTTP connection + inference adapter) for the
    /// endpoint. The injected model id is treated as a REMOTE/OpenAI inference id and
    /// preserved verbatim — never round-tripped through <see cref="ModelReference"/>'s
    /// <c>:latest</c> defaulting — so ids like <c>gpt-4o-mini</c> are sent unchanged.
    /// </summary>
    private static IModelRunner CreateRunner(ModelRunnerEndpoint endpoint, string modelId, string apiKey)
    {
      var connection = new ModelApiConnection(endpoint, apiKey: apiKey);
      var inference = new DockerApiModelInferenceDriver(connection, endpoint);

      InferenceModelId? inferenceId = string.IsNullOrWhiteSpace(modelId) ? null : new InferenceModelId(modelId);
      // DefaultModel remains a Docker artifact reference for display/compat; the
      // inference body is fed the verbatim id via defaultInferenceId.
      var model = ModelReference.TryParse(modelId, out var reference) ? reference : null;

      return new GenericOpenAiModelRunner(endpoint, model, inference, connection.PingAsync, connection, inferenceId);
    }
  }
}
