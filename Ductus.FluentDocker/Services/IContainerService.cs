using System;

namespace Ductus.FluentDocker.Services
{
  public interface IContainerService
  {
    string Id { get; }

    Uri DockerHost { get; }
  }
}
