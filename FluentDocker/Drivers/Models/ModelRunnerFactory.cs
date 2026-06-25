using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;

namespace FluentDocker.Drivers.Models
{
  /// <summary>
  /// Factory that composes the Docker-specific types (<see cref="ModelApiConnection"/> and
  /// <see cref="OpenAiModelInferenceDriver"/>) needed to back an inference runner. Placing
  /// this composition here — in the Drivers layer — removes the layering leak that previously
  /// lived in <c>Services.ModelRunnerEnvironment</c>.
  /// </summary>
  public static class ModelRunnerFactory
  {
    /// <summary>
    /// Creates an inference-only runner that speaks OpenAI JSON over the given endpoint.
    /// </summary>
    /// <param name="endpoint">The target endpoint.</param>
    /// <param name="modelId">
    /// The verbatim model id to use in inference request bodies. When null or empty the runner
    /// has no default model and callers must supply one per request.
    /// </param>
    /// <param name="apiKey">Optional bearer token for the endpoint.</param>
    /// <returns>
    /// A <see cref="FluentDocker.Services.IInferenceModelRunner"/> whose disposal releases the
    /// underlying HTTP connection. The concrete type also satisfies
    /// <see cref="FluentDocker.Services.IModelRunner"/> so existing callers are unaffected.
    /// </returns>
    public static Services.IInferenceModelRunner CreateInferenceRunner(
        ModelRunnerEndpoint endpoint, string modelId, string apiKey = null)
    {
      var connection = new ModelApiConnection(endpoint, apiKey: apiKey);
      var inference = new OpenAiModelInferenceDriver(connection, endpoint);
      InferenceModelId? inferenceId = string.IsNullOrWhiteSpace(modelId) ? null : new InferenceModelId(modelId);
      var model = ModelReference.TryParse(modelId, out var r) ? r : null;
      return new Services.Impl.GenericOpenAiModelRunner(endpoint, model, inference, connection.PingAsync, connection, inferenceId);
    }
  }
}
