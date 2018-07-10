using System;
#if COREFX
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#endif

namespace Ductus.FluentDocker.Common
{
  internal static class Logger
  {
    internal static bool Enabled = true;

    public static void Log(string message)
    {
      if (!Enabled) return;

#if COREFX
      ILogger.Value.LogTrace(message);
    }


    private static readonly Lazy<ILogger> ILogger
      = new Lazy<ILogger>(() =>
      {
        var factory = new ServiceCollection().AddLogging().BuildServiceProvider().GetRequiredService<ILoggerFactory>();
        factory.AddDebug();
        return factory.CreateLogger(Constants.DebugCategory);
      });
#else
			System.Diagnostics.Debugger.Log((int)System.Diagnostics.TraceLevel.Verbose, Constants.DebugCategory, message);
		}
#endif
  }
}