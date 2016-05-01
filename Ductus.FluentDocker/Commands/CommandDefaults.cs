using System;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Commands
{
  public static class CommandDefaults
  {
    static CommandDefaults()
    {
      AutoDetect();
    }

    public static string MachineDriver { get; set; } = "virtualbox";
    /// <summary>
    /// TODO: This property is currently not used!
    /// </summary>
    public static string MachineExtraDefaultCreateArgs { get; set; } = string.Empty;

    /// <summary>
    /// Tries to autodetect the <see cref="CommandDefaults"/>.
    /// </summary>
    /// <remarks>
    /// http://docker-saigon.github.io/post/Docker-Beta/
    /// </remarks>
    public static void AutoDetect()
    {
      if (Environment.OSVersion.IsWindows() || Environment.OSVersion.IsMac())
      {
        // Check if native boo2docker for non linux
        // Prefer that instead of machine
        if (!string.IsNullOrEmpty(CommandExtensions.GetBoot2DockerNativeBinPath()))
        {
          MachineDriver = Environment.OSVersion.IsWindows() ? "hyperv" : "xhyve";

          if (Environment.OSVersion.IsWindows())
          {
            // TODO: Is it possible instead to use the proxy to proxy this machine
            // TODO: for the default docker NAT switch instead?
            MachineExtraDefaultCreateArgs = "--hyperv-virtual-switch external-switch {0}";
          }
        }
      }
    }
  }
}
