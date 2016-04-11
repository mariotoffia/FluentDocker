using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using Ductus.FluentDocker.Model;

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
    ///   The client certificate to use when communicating with the docker daemon.
    /// </summary>
    X509Certificate2 ClientCertificate { get; }

    /// <summary>
    ///   The Ca certificate singed the client certificate.
    /// </summary>
    X509Certificate2 ClientCaCertificate { get; }

    /// <summary>
    /// Gets a new copy of a set of running <see cref="IContainerService"/>s.
    /// </summary>
    /// <remarks>
    /// This will give back new list for each call and it will scan the <see cref="IHostService"/> each
    /// time so this operation may be time consuming.
    /// </remarks>
    IList<IContainerService> RunningContainers { get; }

    IList<IContainerService> GetContainers(bool all = true, string filter = null);

    /// <summary>
    /// Creates a new container (not started).
    /// </summary>
    /// <param name="image">The image to base the container from.</param>
    /// <param name="command">Optionally a command to run when it is started.</param>
    /// <param name="args">Optionally a set of parameters to go with the <see cref="command"/> when started.</param>
    /// <param name="prms">Optionally paramters to configure the container.</param>
    /// <returns>A service reflecting the newly created container.</returns>
    /// <exception cref="FluentDockerException">If error occurs.</exception>
    IContainerService Create(string image, string command = null,
      string[] args = null, ContainerCreateParams prms = null);
  }
}