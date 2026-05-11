using System.IO;

namespace FluentDocker.Resources
{
  public interface IResourceWriter
  {
    IResourceWriter Write(ResourceStream stream);
    IResourceWriter Write(ResourceReader resources);
  }
}
