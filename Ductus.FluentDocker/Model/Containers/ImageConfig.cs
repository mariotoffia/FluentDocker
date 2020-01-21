using System;
using System.Collections.Generic;
// ReSharper disable ClassNeverInstantiated.Global

namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class ImageConfig
  {
    public string Id { get; set; }
    public string[] RepoTags { get; set; }
    public string[] RepoDigests { get; set; }
    public string Parent { get; set; }
    public string Comment { get; set; }
    public DateTime Created { get; set; }
    public string Container { get; set; }
    public ContainerConfig ContainerConfig { get; set; }
    public string DockerVersion { get; set; }
    public string Author { get; set; }
    public ContainerConfig Config { get; set; }
    public string Architecture { get; set; }
    public string Os { get; set; }
    public long Size { get; set; }
    public long VirtualSize { get; set; }
    public GraphDriver GraphDriver { get; set; }
    public FileSystem RootFs { get; set; }
    public IDictionary<string, string> Metadata { get; set; }
  }

  public sealed class GraphDriver
  {
    public string Name { get; set; }
    public GraphDriverData Data { get; set; }
  }

  public sealed class GraphDriverData
  {
    public string LowerDir { get; set; }
    public string MergedDir { get; set; }
    public string UpperDir { get; set; }
    public string WorkDir { get; set; }
  }

  public sealed class FileSystem
  {
    public string Type { get; set; }
    public string[] Layers { get; set; }
  }
}
