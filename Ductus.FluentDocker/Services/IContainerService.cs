using System;
using System.Collections.Generic;
using System.Net;
using Ductus.FluentDocker.Model;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services
{
  public interface IContainerService : IService
  {
    string Id { get; }

    Uri DockerHost { get; }

    /// <summary>
    ///   Gets the configuration from the docker host for this container.
    /// </summary>
    /// <param name="fresh">If a new copy is wanted or a cached one. If non has been requested it will fetch one and cache it.</param>
    /// <remarks>
    ///   This is not cached, thus it will go to the docker daemon each time.
    /// </remarks>
    Container GetConfiguration(bool fresh = false);

    /// <summary>
    ///   Translates a docker exposed port and protocol (on format 'port/proto' e.g. '534/tcp') to a
    ///   host endpoint that can be contacted outside the container.
    /// </summary>
    /// <param name="portAndProto">The port slash protocol to translate to a host based <see cref="IPEndPoint" />.</param>
    /// <returns>A host based endpoint from a exposed port or null if none.</returns>
    IPEndPoint ToHosExposedtPort(string portAndProto);

    /// <summary>
    ///   Gets the running processes within the container.
    /// </summary>
    /// <returns>A <see cref="Processes" /> instance with one or more process rows.</returns>
    Processes GetRunningProcesses();

    /// <summary>
    ///   Exports the container to specified fqPath.
    /// </summary>
    /// <param name="fqPath">A fqPath (with filename) to export to.</param>
    /// <param name="explode">
    ///   If set to true, this will become the directory instead where the exploded tar file will reside,
    ///   default is false.
    /// </param>
    /// <returns>The fqPath to the tar file. If failure null is returned.</returns>
    string Export(TemplateString fqPath, bool explode = false);

    /// <summary>
    ///   Copies file or directory from the <paramref name="hostPath" /> to the containers <paramref name="containerPath" />.
    /// </summary>
    /// <param name="containerPath">The container path to copy to.</param>
    /// <param name="hostPath">The host path to copy from.</param>
    /// <returns>The path where the files where copied from if successful, otherwise null is returned.</returns>
    string CopyTo(TemplateString containerPath, TemplateString hostPath);

    /// <summary>
    ///   Copies file or directory from the <paramref name="containerPath" /> to the host <paramref name="hostPath" />.
    /// </summary>
    /// <param name="containerPath">The container path to copy from.</param>
    /// <param name="hostPath">The host path to copy to.</param>
    /// <returns>The path where the files where copied to if successful, otherwise null is returned.</returns>
    string CopyFrom(TemplateString containerPath, TemplateString hostPath);

    /// <summary>
    ///   Diffs the container from it's orignal state to the current state.
    /// </summary>
    /// <returns>
    ///   An array, zero or more, <see cref="Diff" /> instances, reflecting the changes from the container
    ///   starting point (when it has started).
    /// </returns>
    IList<Diff> Diff();
  }
}