using System;
using System.Collections.Generic;
using System.Net;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Machines;

namespace Ductus.FluentDocker.Services
{
  /// <summary>
  ///   Represents a docker host, either native or a virtual machine (local or remote).
  /// </summary>
  public interface IHostService : IService
  {
    /// <summary>
    ///   The address and port to the docker daemon.
    /// </summary>
    DockerUri Host { get; }

    /// <summary>
    ///   Gets a value whether the <see cref="Host" /> is a native docker (local or remote) or a daemon running
    ///   in a virtual machine.
    /// </summary>
    bool IsNative { get; }

    /// <summary>
    ///   Gets a value whether it needs TLS or not to connect to the docker daemon.
    /// </summary>
    bool RequireTls { get; }

    /// <summary>
    ///   The certificates if any needed for this host.
    /// </summary>
    ICertificatePaths Certificates { get; }

    /// <summary>
    ///   Gets a new copy of a set of running <see cref="IContainerService" />s.
    /// </summary>
    /// <remarks>
    ///   This will give back new list for each call and it will scan the <see cref="IHostService" /> each
    ///   time so this operation may be time consuming.
    /// </remarks>
    IList<IContainerService> GetRunningContainers();

    IList<IContainerService> GetContainers(bool all = true, string filter = null);

    IList<IContainerImageService> GetImages(bool all = true, string filer = null);

    /// <summary>
    ///   Creates a new container (not started).
    /// </summary>
    /// <param name="image">The image to base the container from.</param>
    /// <param name="forcePull">If the image shall be forced downloaded or not. Default is false.</param>
    /// <param name="prms">Optionally parameters to configure the container.</param>
    /// <param name="stopOnDispose">If the docker container shall be stopped when service is disposed.</param>
    /// <param name="deleteOnDispose">If the docker container shall be deleted when the service is disposed.</param>
    /// <param name="deleteVolumeOnDispose">If the associated volumes should be deleted when container is disposed.</param>
    /// <param name="deleteNamedVolumeOnDispose">If associated named volumes should be deleted as well.</param>
    /// <param name="command">Optionally a command to run when it is started.</param>
    /// <param name="args">Optionally a set of parameters to go with the <see cref="command" /> when started.</param>
    /// <param name="customEndpointResolver">Set this resolver when creating the container.</param>
    /// <returns>A service reflecting the newly created container.</returns>
    /// <exception cref="FluentDockerException">If error occurs.</exception>
    IContainerService Create(string image, bool forcePull = false, ContainerCreateParams prms = null,
      bool stopOnDispose = true, bool deleteOnDispose = true, bool deleteVolumeOnDispose = false,
      bool deleteNamedVolumeOnDispose = false, string command = null,
      string[] args = null, 
      Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> customEndpointResolver = null);

    /// <summary>
    ///   Gets all the docker networks.
    /// </summary>
    /// <returns>A list with zero or more docker networks.</returns>
    IList<INetworkService> GetNetworks();

    /// <summary>
    ///   Creates a single network.
    /// </summary>
    /// <param name="name">The name of the network</param>
    /// <param name="createParams">Optional additional parameters to customize the network creation.</param>
    /// <param name="removeOnDispose">If the network shall be removed when service is disposed.</param>
    /// <returns>A network service if the newly created network.</returns>
    /// <exception cref="FluentDockerException">If fails to create the docker network.</exception>
    INetworkService CreateNetwork(string name, NetworkCreateParams createParams = null, bool removeOnDispose = false);

    /// <summary>
    /// Retrieves volumes.
    /// </summary>
    /// <returns>A list with zero or more volumes.</returns>
    IList<IVolumeService> GetVolumes();

    /// <summary>
    /// Creates a volume.
    /// </summary>
    /// <param name="name">Optional the unique name of the volume.</param>
    /// <param name="driver">Optional the volume driver to use.</param>
    /// <param name="labels">Optional labels as metadata for the volume.</param>
    /// <param name="opts">Optional parameters to feed to the specified or default driver.</param>
    /// <param name="removeOnDispose">If volume shall be remove when disposed or not. Default is false.</param>
    /// <returns></returns>
    IVolumeService CreateVolume(string name = null, string driver = null /*local*/, string[] labels = null, IDictionary<string, string> opts = null, bool removeOnDispose = false);

    /// <summary>
    ///   Gets the machine configuration if machine.
    /// </summary>
    /// <returns>A machine configuration. It will always return null if native.</returns>
    MachineConfiguration GetMachineConfiguration();
  }
}
