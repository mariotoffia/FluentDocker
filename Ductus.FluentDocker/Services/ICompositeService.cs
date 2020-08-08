using System.Collections.Generic;
using System.Diagnostics;

namespace Ductus.FluentDocker.Services
{
  public interface ICompositeService : IService
  {
    event DataReceivedEventHandler OutputDataReceived;
    event DataReceivedEventHandler ErrorDataReceived;
    IReadOnlyCollection<IHostService> Hosts { get; }
    IReadOnlyCollection<IContainerService> Containers { get; }
    IReadOnlyCollection<IContainerImageService> Images { get; }
    IReadOnlyCollection<IService> Services { get; }
    new ICompositeService Start();
  }
}
