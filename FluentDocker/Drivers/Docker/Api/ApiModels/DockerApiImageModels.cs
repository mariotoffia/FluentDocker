using Newtonsoft.Json;

namespace FluentDocker.Drivers.Docker.Api.ApiModels
{
  /// <summary>
  /// NDJSON progress line from image pull operations.
  /// </summary>
  internal class PullProgressLine
  {
    [JsonProperty("status")] public string Status { get; set; }
    [JsonProperty("progress")] public string Progress { get; set; }
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("progressDetail")] public ProgressDetail ProgressDetail { get; set; }
    [JsonProperty("error")] public string Error { get; set; }
  }

  /// <summary>
  /// NDJSON progress line from image push operations.
  /// </summary>
  internal class PushProgressLine
  {
    [JsonProperty("status")] public string Status { get; set; }
    [JsonProperty("progress")] public string Progress { get; set; }
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("progressDetail")] public ProgressDetail ProgressDetail { get; set; }
    [JsonProperty("error")] public string Error { get; set; }
  }

  /// <summary>
  /// NDJSON line from image build output.
  /// </summary>
  internal class BuildOutputLine
  {
    [JsonProperty("stream")] public string Stream { get; set; }
    [JsonProperty("error")] public string Error { get; set; }
    [JsonProperty("errorDetail")] public ErrorDetail ErrorDetail { get; set; }
    [JsonProperty("aux")] public BuildAux Aux { get; set; }
  }

  internal class BuildAux
  {
    [JsonProperty("ID")] public string Id { get; set; }
  }

  internal class ProgressDetail
  {
    [JsonProperty("current")] public long Current { get; set; }
    [JsonProperty("total")] public long Total { get; set; }
  }

  internal class ErrorDetail
  {
    [JsonProperty("message")] public string Message { get; set; }
    [JsonProperty("code")] public int Code { get; set; }
  }
}
