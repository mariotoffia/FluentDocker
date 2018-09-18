using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services
{
  public interface IContainerService : IService
  {
    /// <summary>
    ///   The container id of the running container.
    /// </summary>
    string Id { get; }

    /// <summary>
    ///   The <see cref="System.Uri" /> to the docker daemon in control of this service.
    /// </summary>
    DockerUri DockerHost { get; }

    /// <summary>
    ///   When set to true the container is stopped automatically on <see cref="IDisposable.Dispose()" />.
    /// </summary>
    bool StopOnDispose { get; set; }

    /// <summary>
    ///   When set to true the container is removed automaticallyh on <see cref="IDisposable.Dispose()" />.
    /// </summary>
    bool RemoveOnDispose { get; set; }

    /// <summary>
    ///   Dettermines if this container is based on a windows image or linux image.
    /// </summary>
    bool IsWindowsContainer { get; }

    /// <summary>
    ///   Paths to where certificates resides for this service.
    /// </summary>
    ICertificatePaths Certificates { get; }
    
    /// <summary>
    /// The image the running container is based on.
    /// </summary>
    IContainerImageService Image { get; }

    /// <summary>
    ///   Gets the configuration from the docker host for this container.
    /// </summary>
    /// <param name="fresh">If a new copy is wanted or a cached one. If non has been requested it will fetch one and cache it.</param>
    /// <remarks>
    ///   This is not cached, thus it will go to the docker daemon each time.
    /// </remarks>
    Container GetConfiguration(bool fresh = false);

    /// <summary>
    ///   Overridden to handle fluent access.
    /// </summary>
    /// <returns></returns>
    new IContainerService Start();

    /// <summary>
    ///   Gets all volumes attached to this container.
    /// </summary>
    /// <returns>A list with zero or more volumes.</returns>
    IList<IVolumeService> GetVolumes();

    /// <summary>
    ///   Gets all networks that this container is attached to.
    /// </summary>
    /// <returns>A list with one or more networks.</returns>
    IList<INetworkService> GetNetworks();
  }
}