using System;

#if COREFX
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#endif

namespace Ductus.FluentDocker.Common
{
	internal static class Logger
	{
		public static void Log(string message)
		{
#if COREFX
			ILogger.Value.LogTrace(message);
		}



		private static readonly Lazy<ILogger> ILogger
			= new Lazy<ILogger>(() =>
			{
				var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
				provider.GetRequiredService<ILoggerFactory>().AddDebug();
				return provider.GetRequiredService<ILoggerProvider>().CreateLogger(Constants.DebugCategory);
			});
#else
			System.Diagnostics.Debugger.Log((int)System.Diagnostics.TraceLevel.Verbose, Constants.DebugCategory, message);
		}
#endif
	}
}
