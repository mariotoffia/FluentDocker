using System;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Services
{
  public interface IContainerService
  {
    string Id { get; }

    Uri DockerHost { get; }

    /// <summary>
    /// Gets the configuration from the docker host for this container.
    /// </summary>
    Container Configuration { get; }
  }
}
