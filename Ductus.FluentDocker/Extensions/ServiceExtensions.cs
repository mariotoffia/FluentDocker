using System;
using System.Threading;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Extensions
{
	public static class ServiceExtensions
	{
		public static void WaitForRunning(this IContainerService container)
		{
			Exception exception = null;
			using (var mre = new ManualResetEventSlim())
			using (new Timer(_ =>
			{
				var state = container.GetConfiguration(true).State;
				if (!string.IsNullOrWhiteSpace(state.Error))
				{
					exception = new FluentDockerException($"Unable to start container: {state.Error}");
					mre.Set();
				}
				if (state.ToServiceState() == ServiceRunningState.Running)
					mre.Set();
			}, null, 0, 500))
				mre.Wait();
			if (exception != null)
				throw exception;
		}
	}
}
