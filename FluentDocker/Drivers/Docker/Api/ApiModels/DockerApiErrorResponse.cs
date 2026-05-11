using System.Text.Json.Serialization;

namespace FluentDocker.Drivers.Docker.Api.ApiModels
{
  /// <summary>
  /// Error response from the Docker Engine REST API.
  /// </summary>
  internal sealed class DockerApiErrorResponse
  {
    [JsonPropertyName("message")]
    public string Message { get; set; }
  }
}
