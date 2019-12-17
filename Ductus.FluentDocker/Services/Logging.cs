using Ductus.FluentDocker.Common;

namespace Ductus.FluentDocker.Services
{
  /// <summary>
  ///   Controls Logging.
  /// </summary>
  public static class Logging
  {
    /// <summary>
    ///   Enables logging.
    /// </summary>
    public static void Enabled()
    {
      Logger.Enabled = true;
    }

    /// <summary>
    ///   Disables logging.
    /// </summary>
    public static void Disabled()
    {
      Logger.Enabled = false;
    }
  }
}
