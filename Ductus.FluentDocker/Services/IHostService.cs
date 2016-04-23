using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using Ductus.FluentDocker.Common;
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
    ///   The adress and port to the docker daemon.
    /// </summary>
    Uri Host { get; }

    /// <summary>
    ///   Gets a value wether the <see cref="Host" /> is a native docker (local or remote) or a daemon running
    ///   in a virtual machine.
    /// </summary>
    bool IsNative { get; }

    /// <summary>
    ///   Gets a value wether it needs TLS or not to connect to the docker daemon.
    /// </summary>
    bool RequireTls { get; }

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
    /// <param name="prms">Optionally paramters to configure the container.</param>
    /// <param name="stopOnDispose">If the docker container shall be stopped when service is disposed.</param>
    /// <param name="deleteOnDispose">If the docker container shall be deleted when the service is disposed.</param>
    /// <param name="command">Optionally a command to run when it is started.</param>
    /// <param name="args">Optionally a set of parameters to go with the <see cref="command" /> when started.</param>
    /// <returns>A service reflecting the newly created container.</returns>
    /// <exception cref="FluentDockerException">If error occurs.</exception>
    IContainerService Create(string image, ContainerCreateParams prms = null,
      bool stopOnDispose = true, bool deleteOnDispose = true, string command = null,
      string[] args = null);

    /// <summary>
    ///   Gets the machine configuration if machine.
    /// </summary>
    /// <returns>A machine configuration. It will always return null if native.</returns>
    MachineConfiguration GetMachineConfiguration();
  }
}