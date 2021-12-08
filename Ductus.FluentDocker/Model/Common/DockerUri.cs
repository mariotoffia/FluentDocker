using System;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Model.Common
{
  public sealed class DockerUri : Uri
  {
    private const string DockerHost = "DOCKER_HOST";
    private const string DockerHostUrlWindowsNative = "npipe://./pipe/docker_engine";
    private const string DockerHostUrlLegacy = "tcp://localhost:2375";
    private const string DockerHostUrlMacOrLinux = "unix:///var/run/docker.sock";

    public DockerUri(string uriString) : base(uriString)
    {
      if (uriString == DockerHostUrlMacOrLinux || uriString == DockerHostUrlWindowsNative)
        this.IsStandardDaemon = true;
    }

    public static string GetDockerHostEnvironmentPathOrDefault()
    {
      var env = Environment.GetEnvironmentVariable(DockerHost);
      if (null != env)
      {
        return env;
      }

      if (FdOs.IsWindows())
      {
        return CommandExtensions.IsToolbox() ? DockerHostUrlLegacy : DockerHostUrlWindowsNative;
      }

      return CommandExtensions.IsToolbox() ? DockerHostUrlLegacy : DockerHostUrlMacOrLinux;
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
