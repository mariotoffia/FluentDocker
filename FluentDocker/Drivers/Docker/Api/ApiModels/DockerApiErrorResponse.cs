using Newtonsoft.Json;

namespace FluentDocker.Drivers.Docker.Api.ApiModels
{
  /// <summary>
  /// Error response from the Docker Engine REST API.
  /// </summary>
  internal class DockerApiErrorResponse
  {
    [JsonProperty("message")]
    public string Message { get; set; }
  }
}
