#nullable enable
namespace FluentDocker.Model.Images
{
  /// <summary>
  /// One repository entry from an image listing (e.g. <c>docker images</c>), with all its tags grouped together.
  /// </summary>
  public sealed class DockerImageRowResponse
  {
    /// <summary>The image's content-addressable ID (e.g. <c>sha256:...</c>).</summary>
    public string? Id { get; set; }

    /// <summary>The repository name (e.g. <c>nginx</c>).</summary>
    public string? Name { get; set; }

    /// <summary>All tags in this repository that resolve to <see cref="Id"/> (e.g. <c>latest</c>, <c>1.25</c>).</summary>
    public string[]? Tags { get; set; }
  }
}
