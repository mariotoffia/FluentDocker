using System.Diagnostics;

namespace FluentDocker.Common
{
  /// <summary>
  /// Simple diagnostic logger that writes to <see cref="Trace"/> under the FluentDocker category.
  /// </summary>
  public static class Logger
  {
    internal static bool Enabled = true;

    /// <summary>Writes a diagnostic message to the trace output if logging is enabled.</summary>
    /// <param name="message">The message to log.</param>
    public static void Log(string message)
    {
      if (!Enabled)
        return;

      Trace.WriteLine(message, Constants.DebugCategory);
    }
  }
}
