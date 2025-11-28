using FluentDocker.Extensions;

namespace FluentDocker.Commands
{
  /// <summary>
  /// Default settings for Docker commands.
  /// </summary>
  /// <remarks>
  /// This class is deprecated. Configuration should be done through the Driver layer instead.
  /// </remarks>
  [System.Obsolete("Configuration should be done through the Driver layer. Will be removed in v4.0.0.")]
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
      if (Common.FdOs.IsWindows() || Common.FdOs.IsOsx())
      {
        // Prefer non toolbox on windows and mac
        if (!CommandExtensions.IsToolbox())
        {
          MachineDriver = Common.FdOs.IsWindows() ? "hyperv" : "xhyve";

          if (Common.FdOs.IsWindows())
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
