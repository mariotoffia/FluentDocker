namespace Ductus.FluentDocker.Model.Images
{
  public sealed class DockerImageRowResponse
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string []Tags { get; set; }
  }
}
