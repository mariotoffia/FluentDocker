using System.Text.Json.Serialization;

namespace FluentDocker.Drivers.Docker.Api.ApiModels
{
  /// <summary>
  /// NDJSON progress line from image pull operations.
  /// </summary>
  internal sealed class PullProgressLine
  {
    [JsonPropertyName("status")] public string Status { get; set; }
    [JsonPropertyName("progress")] public string Progress { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("progressDetail")] public ProgressDetail ProgressDetail { get; set; }
    [JsonPropertyName("error")] public string Error { get; set; }
  }

  /// <summary>
  /// NDJSON progress line from image push operations.
  /// </summary>
  internal sealed class PushProgressLine
  {
    [JsonPropertyName("status")] public string Status { get; set; }
    [JsonPropertyName("progress")] public string Progress { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("progressDetail")] public ProgressDetail ProgressDetail { get; set; }
    [JsonPropertyName("error")] public string Error { get; set; }
  }

  /// <summary>
  /// NDJSON line from image build output.
  /// </summary>
  internal sealed class BuildOutputLine
  {
    [JsonPropertyName("stream")] public string Stream { get; set; }
    [JsonPropertyName("error")] public string Error { get; set; }
    [JsonPropertyName("errorDetail")] public ErrorDetail ErrorDetail { get; set; }
    [JsonPropertyName("aux")] public BuildAux Aux { get; set; }
  }

  internal sealed class BuildAux
  {
    [JsonPropertyName("ID")] public string Id { get; set; }
  }

  internal sealed class ProgressDetail
  {
    [JsonPropertyName("current")] public long Current { get; set; }
    [JsonPropertyName("total")] public long Total { get; set; }
  }

  internal sealed class ErrorDetail
  {
    [JsonPropertyName("message")] public string Message { get; set; }
    [JsonPropertyName("code")] public int Code { get; set; }
  }
}
