using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Drivers.Docker.Api.ApiModels
{
  /// <summary>
  /// Source-generated JSON serialization context for all Docker API models.
  /// Eliminates reflection-based serialization overhead and enables Native AOT / trimming.
  /// </summary>
  [JsonSourceGenerationOptions(
      PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      GenerationMode = JsonSourceGenerationMode.Default)]
  // Container models — requests
  [JsonSerializable(typeof(CreateContainerRequest))]
  [JsonSerializable(typeof(HostConfigRequest))]
  [JsonSerializable(typeof(DeviceMappingRequest))]
  [JsonSerializable(typeof(PortBindingRequest))]
  [JsonSerializable(typeof(RestartPolicyRequest))]
  [JsonSerializable(typeof(NetworkingConfigRequest))]
  [JsonSerializable(typeof(EndpointConfigRequest))]
  [JsonSerializable(typeof(IpamConfigRequest))]
  [JsonSerializable(typeof(HealthcheckRequest))]
  [JsonSerializable(typeof(ExecCreateRequest))]
  [JsonSerializable(typeof(ExecStartRequest))]
  [JsonSerializable(typeof(UpdateContainerRequest))]
  // Container models — responses
  [JsonSerializable(typeof(CreateContainerResponse))]
  [JsonSerializable(typeof(ExecCreateResponse))]
  [JsonSerializable(typeof(ExecInspectResponse))]
  [JsonSerializable(typeof(WaitContainerResponse))]
  [JsonSerializable(typeof(WaitContainerError))]
  // Image models (NDJSON)
  [JsonSerializable(typeof(PullProgressLine))]
  [JsonSerializable(typeof(PushProgressLine))]
  [JsonSerializable(typeof(BuildOutputLine))]
  [JsonSerializable(typeof(BuildAux))]
  [JsonSerializable(typeof(ProgressDetail))]
  [JsonSerializable(typeof(ErrorDetail))]
  // Error response
  [JsonSerializable(typeof(DockerApiErrorResponse))]
  internal sealed partial class DockerApiJsonContext : JsonSerializerContext { }
}
