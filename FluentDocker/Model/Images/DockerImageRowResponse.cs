#nullable enable
namespace FluentDocker.Model.Images
{
  public sealed class DockerImageRowResponse
  {
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string[] Tags { get; set; } = null!;
  }
}
