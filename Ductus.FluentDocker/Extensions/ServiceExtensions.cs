using System;
using System.Threading;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Extensions
{
	public static class ServiceExtensions
	{
		private static void WaitForState(this IContainerService container, ServiceRunningState state)
		{
			Exception exception = null;
			using (var mre = new ManualResetEventSlim())
			using (new Timer(_ =>
			{
				var containerState = container.GetConfiguration(true).State;
				if (!string.IsNullOrWhiteSpace(containerState.Error))
				{
					exception = new FluentDockerException($"Unable to start container: {containerState.Error}");
					mre.Set();
				}
				if (containerState.ToServiceState() == state)
					mre.Set();
			}, null, 0, 500))
				mre.Wait();
			if (exception != null)
				throw exception;
		}

		public static void WaitForRunning(this IContainerService container) =>
			WaitForState(container, ServiceRunningState.Running);

		public static void WaitForStopped(this IContainerService container) =>
			WaitForState(container, ServiceRunningState.Stopped);
	}
}
