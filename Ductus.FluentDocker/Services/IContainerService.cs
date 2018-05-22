using System.Collections.Generic;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services
{
  public interface IContainerService : IService
  {
    string Id { get; }
    DockerUri DockerHost { get; }

    /// <summary>
    /// Dettermines if this container is based on a windows image or linux image.
    /// </summary>
    bool IsWindowsContainer { get; }

    /// <summary>
    ///   Paths to where certificates resides for this service.
    /// </summary>
    ICertificatePaths Certificates { get; }

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