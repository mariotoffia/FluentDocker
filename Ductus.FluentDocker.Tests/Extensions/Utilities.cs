using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Tests.Extensions
{
  public static class Utilities
  {
    public static void LinuxMode()
    {
      if (OperatingSystem.IsWindows())
        Info.LinuxDaemon(null);
    }

    /// <summary>
    /// Sets the docker daemon to linux if on windows system.
    /// </summary>
    /// <param name="host">The uri to host, may be null for default.</param>
    /// <param name="certificates">The certificates to communicate, many be null.</param>
    public static void LinuxMode(this DockerUri host, ICertificatePaths certificates = null)
    {
      if (OperatingSystem.IsWindows())
        host.LinuxDaemon(certificates);
    }
  }
}