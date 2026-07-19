using System.Text.Json.Serialization;

namespace FluentDocker.Drivers.Docker.Api.ApiModels
{
  /// <summary>
  /// Success response from Docker Engine <c>POST /auth</c>. Token registries (Docker Hub
  /// PAT/2FA, some cloud registries) return an <c>IdentityToken</c> to be sent in subsequent
  /// <c>X-Registry-Auth</c> headers instead of the raw credentials.
  /// </summary>
  internal sealed class DockerApiAuthResponse
  {
    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    [JsonPropertyName("IdentityToken")]
    public string? IdentityToken { get; set; }
  }
}
