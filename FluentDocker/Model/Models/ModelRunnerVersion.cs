#nullable enable
namespace FluentDocker.Model.Models
{
  /// <summary>
  /// Version information for the runner (the result of <c>docker model version</c>).
  /// </summary>
  public sealed class ModelRunnerVersion
  {
    /// <summary>The CLI plugin version, e.g. <c>v1.2.1</c>.</summary>
    public string? CliVersion { get; init; }

    /// <summary>The engine build, e.g. the llama.cpp build hash.</summary>
    public string? EngineVersion { get; init; }

    /// <summary>The API version.</summary>
    public string? ApiVersion { get; init; }
  }
}
