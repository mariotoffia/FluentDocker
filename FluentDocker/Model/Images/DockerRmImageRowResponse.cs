#nullable enable
namespace FluentDocker.Model.Images
{
  /// <summary>
  /// One line of <c>docker rmi</c>/<c>image rm</c> output, e.g. <c>Untagged: nginx:latest</c> or
  /// <c>Deleted: sha256:...</c>.
  /// </summary>
  public sealed class DockerRmImageRowResponse
  {
    /// <summary>The image or layer ID/reference the line refers to.</summary>
    public string? Id { get; set; }

    /// <summary>The action Docker performed for <see cref="Id"/> (e.g. <c>Untagged</c>, <c>Deleted</c>).</summary>
    public string? Command { get; set; }
  }
}
