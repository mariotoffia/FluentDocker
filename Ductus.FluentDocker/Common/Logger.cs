using System.Diagnostics;

namespace Ductus.FluentDocker.Common
{
  public static class Logger
  {
    internal static bool Enabled = true;

    public static void Log(string message)
    {
      if (!Enabled)
        return;

      Trace.WriteLine(message, Constants.DebugCategory);
    }
  }
}
