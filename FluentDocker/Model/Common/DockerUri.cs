using System;
using FluentDocker.Common;

namespace FluentDocker.Model.Common
{
  public sealed class DockerUri : Uri
  {
    private const string DockerHost = "DOCKER_HOST";
    private const string DockerHostUrlWindowsNative = "npipe://./pipe/docker_engine";
    private const string DockerHostUrlLegacy = "tcp://localhost:2375";
    private const string DockerHostUrlMacOrLinux = "unix:///var/run/docker.sock";

    private static Func<bool> _isToolboxResolver;

    public DockerUri(string uriString) : base(uriString)
    {
      if (uriString == DockerHostUrlMacOrLinux || uriString == DockerHostUrlWindowsNative)
        this.IsStandardDaemon = true;
    }

    /// <summary>
    /// Configures how IsToolbox is resolved. Used by driver infrastructure.
    /// </summary>
    /// <param name="resolver">A function that returns true if Docker Toolbox is in use.</param>
    public static void ConfigureToolboxResolver(Func<bool> resolver)
    {
      _isToolboxResolver = resolver;
    }

    public static string GetDockerHostEnvironmentPathOrDefault()
    {
      var env = Environment.GetEnvironmentVariable(DockerHost);
      if (null != env)
      {
        return env;
      }

      var isToolbox = _isToolboxResolver?.Invoke() ?? IsToolboxFromEnvironment();

      if (FdOs.IsWindows())
      {
        return isToolbox ? DockerHostUrlLegacy : DockerHostUrlWindowsNative;
      }

      return isToolbox ? DockerHostUrlLegacy : DockerHostUrlMacOrLinux;
    }

    /// <summary>
    /// Fallback check for Docker Toolbox using environment variable.
    /// </summary>
    private static bool IsToolboxFromEnvironment()
    {
      return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH"));
    }

    /// <summary>
    /// Returns true if the DockerUri has a "standard" daemon URI.
    /// </summary>
    /// <value>True if standard daemon, false otherwise.</value>
    /// <remarks>
    /// If it is a standard daemon URI, there's no need to add the -H flag
    /// </remarks>
    public bool IsStandardDaemon {get;}

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
