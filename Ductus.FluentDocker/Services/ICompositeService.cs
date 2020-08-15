using System.Collections.Generic;

namespace Ductus.FluentDocker.Services
{
  public interface ICompositeService : IService
  {
    IReadOnlyCollection<IHostService> Hosts { get; }
    IReadOnlyCollection<IContainerService> Containers { get; }
    IReadOnlyCollection<IContainerImageService> Images { get; }
    IReadOnlyCollection<IService> Services { get; }
    new ICompositeService Start();
  }
}
