#nullable enable
using System;
using FluentDocker.Common;

namespace FluentDocker.Model.Common
{
  /// <summary>
  /// Represents a Docker host URI.
  /// </summary>
  /// <remarks>
  /// v3.0 Note: Docker Toolbox support has been removed. Uses native Docker daemon URIs only.
  /// </remarks>
  public sealed class DockerUri : Uri
  {
    private const string DockerHost = "DOCKER_HOST";
    private const string DockerHostUrlWindowsNative = "npipe://./pipe/docker_engine";
    private const string DockerHostUrlWindowsNativeCanonical = "npipe:////./pipe/docker_engine";
    private const string DockerHostUrlMacOrLinux = "unix:///var/run/docker.sock";

    /// <summary>Creates a <see cref="DockerUri"/> from a Docker daemon URI string (e.g. <c>unix:///var/run/docker.sock</c>, <c>npipe://./pipe/docker_engine</c>, <c>tcp://host:2375</c>).</summary>
    /// <param name="uriString">The daemon URI to parse.</param>
    public DockerUri(string uriString) : base(uriString)
    {
      if (uriString == DockerHostUrlMacOrLinux ||
          uriString == DockerHostUrlWindowsNative ||
          uriString == DockerHostUrlWindowsNativeCanonical)
        IsStandardDaemon = true;
    }

    /// <summary>
    /// Gets the Docker host URI from the DOCKER_HOST environment variable or returns the platform default.
    /// An empty DOCKER_HOST is treated as unset (Docker convention).
    /// </summary>
    /// <remarks>
    /// Docker CLI contexts are not resolved here: DOCKER_CONTEXT and
    /// ~/.docker/config.json currentContext are ignored. Users of colima, podman-machine,
    /// rootless Docker, or non-default contexts must set DOCKER_HOST explicitly.
    /// </remarks>
    /// <returns>The Docker host URI string.</returns>
    public static string GetDockerHostEnvironmentPathOrDefault()
    {
      var env = Environment.GetEnvironmentVariable(DockerHost);
      if (!string.IsNullOrEmpty(env))
      {
        return env;
      }

      return FdOs.IsWindows() ? DockerHostUrlWindowsNative : DockerHostUrlMacOrLinux;
    }

    /// <summary>
    /// Returns true if the DockerUri has a "standard" daemon URI.
    /// </summary>
    /// <value>True if standard daemon, false otherwise.</value>
    /// <remarks>
    /// If it is a standard daemon URI, there's no need to add the -H flag
    /// </remarks>
    public bool IsStandardDaemon { get; }

    /// <summary>
    /// Renders the URI back to its daemon-connection string form, preserving the quirks
    /// <see cref="Uri"/> normalization would otherwise mangle (trailing slash on <c>ssh</c>,
    /// <c>npipe</c> authority slashes and custom/Podman pipe names).
    /// </summary>
    public override string ToString()
    {
      var baseString = base.ToString();

      if (Scheme == "ssh")
        return baseString.TrimEnd('/');

      // Work from OriginalString, not base.ToString(): Uri normalization dot-segment-collapses the
      // "." host marker and mangles custom pipe names (npipe:////./pipe/custom -> npipe://////pipe/custom).
      // An already-canonical npipe:////<...> form (incl. custom/Podman pipe names) is returned verbatim;
      // only the legacy two-slash npipe://<...> form gets the missing authority slashes added (MDL-MAJ-1).
      if (Scheme == "npipe")
      {
        var source = OriginalString ?? baseString;
        if (source.StartsWith("npipe:////", StringComparison.Ordinal))
          return source;
        if (source.StartsWith("npipe://", StringComparison.Ordinal))
          return string.Concat("npipe:////", source.AsSpan("npipe://".Length));
        return source;
      }

      return baseString;
    }
  }
}
