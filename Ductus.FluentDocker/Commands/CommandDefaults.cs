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
      if (Common.OperatingSystem.IsWindows() || Common.OperatingSystem.IsOsx())
      {
        // Prefer non toolbox on windows and mac
        if (!CommandExtensions.IsToolbox())
        {
          MachineDriver = Common.OperatingSystem.IsWindows() ? "hyperv" : "xhyve";

          if (Common.OperatingSystem.IsWindows())
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
