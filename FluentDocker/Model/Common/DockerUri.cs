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
    private const string DockerHostUrlMacOrLinux = "unix:///var/run/docker.sock";

    public DockerUri(string uriString) : base(uriString)
    {
      if (uriString == DockerHostUrlMacOrLinux || uriString == DockerHostUrlWindowsNative)
        IsStandardDaemon = true;
    }

    /// <summary>
    /// Gets the Docker host URI from the DOCKER_HOST environment variable or returns the platform default.
    /// </summary>
    /// <returns>The Docker host URI string.</returns>
    public static string GetDockerHostEnvironmentPathOrDefault()
    {
      var env = Environment.GetEnvironmentVariable(DockerHost);
      if (null != env)
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

    public override string ToString()
    {
      var baseString = base.ToString();

      if (Scheme == "ssh")
        return baseString.TrimEnd('/');

      if (Scheme == "npipe")
        return baseString.Substring(0, 6) + "//" + baseString.Substring(6);

      return baseString;
    }
  }
}
