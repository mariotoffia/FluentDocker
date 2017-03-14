using System.IO;

namespace Ductus.FluentDocker.Resources
{
  public interface IResourceWriter
  {
    IResourceWriter Write(ResourceStream stream);
    IResourceWriter Write(ResourceReader resources);
  }
}
