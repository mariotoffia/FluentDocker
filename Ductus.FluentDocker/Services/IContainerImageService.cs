using System;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services
{
  public interface IContainerImageService : IService
  {
    string Id { get; }

    string Tag { get; }

    Uri DockerHost { get; }

    /// <summary>
    ///   Paths to where certificates resides for this service.
    /// </summary>
    ICertificatePaths Certificates { get; }

    /// <summary>
    ///   Gets the configuration from the docker host for this image.
    /// </summary>
    /// <param name="fresh">If a new copy is wanted or a cached one. If non has been requested it will fetch one and cache it.</param>
    /// <remarks>
    ///   This is not cached, thus it will go to the docker daemon each time.
    /// </remarks>
    Container GetConfiguration(bool fresh = false);
  }
}