using System.Collections.Generic;

namespace Ductus.FluentDocker.Services
{
  public interface ICompositeService : IService
  {
    IReadOnlyCollection<IHostService> Hosts { get; }
    IReadOnlyCollection<IContainerService> Containers { get; }
    IReadOnlyCollection<IService> Services { get; }
  }
}