using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services.Extensions
{
	public static class ContainerExtensions
	{
		/// <summary>
		///   Read the logs from the container.
		/// </summary>
		/// <param name="service">The container to read the logs from.</param>
		/// <param name="follow">If continious logs is wanted.</param>
		/// <param name="token">The cancellation token for logs, especially needed when <paramref name="follow" /> is set to true.</param>
		/// <returns>A console stream to consume.</returns>
		public static ConsoleStream<string> Logs(this IContainerService service, bool follow = false,
		  CancellationToken token = default(CancellationToken))
		{
			return service.DockerHost.Logs(service.Id, token, follow);
		}

		/// <summary>
		/// Exectues a command with arguments on the running container.
		/// </summary>
		/// <param name="service">The service to execute command in.</param>
		/// <param name="arguments">The command and arguments to pass</param>
		/// <remarks>
		///  This will call the docker exec {containerId} "command and arguments".
		/// </remarks>
		public static CommandResponse<IList<string>> Execute(this IContainerService service, string arguments)
		{
			return service.DockerHost.Execute(service.Id, arguments, service.Certificates);
		}

		/// <summary>
		///   Translates a docker exposed port and protocol (on format 'port/proto' e.g. '534/tcp') to a
		///   host endpoint that can be contacted outside the container.
		/// </summary>
		/// <param name="service">The container to query.</param>
		/// <param name="portAndProto">The port slash protocol to translate to a host based <see cref="IPEndPoint" />.</param>
		/// <returns>A host based endpoint from a exposed port or null if none.</returns>
		public static IPEndPoint ToHostExposedEndpoint(this IContainerService service, string portAndProto)
		{
			return service.GetConfiguration()?.NetworkSettings.Ports.ToHostPort(portAndProto, service.DockerHost);
		}

		/// <summary>
		///   Diffs the container from it's orignal state to the current state.
		/// </summary>
		/// <param name="service">The container to do the diff operation on.</param>
		/// <returns>
		///   An array, zero or more, <see cref="Model.Containers.Diff" /> instances, reflecting the changes from the container
		///   starting point (when it has started).
		/// </returns>
		public static IList<Diff> Diff(this IContainerService service)
		{
			return service.DockerHost.Diff(service.Id, service.Certificates).Data;
		}

		/// <summary>
		///   Diffs the container from it's orignal state to the current state.
		/// </summary>
		/// <param name="service">The container to do the diff operation on.</param>
		/// <param name="result">
		///   An array, zero or more, <see cref="Model.Containers.Diff" /> instances, reflecting the changes from the container
		///   starting point (when it has started).
		/// </param>
		/// <returns>The service itself.</returns>
		public static IContainerService Diff(this IContainerService service, out IList<Diff> result)
		{
			result = service.DockerHost.Diff(service.Id, service.Certificates).Data;
			return service;
		}

		/// <summary>
		///   Exports the container to specified fqPath.
		/// </summary>
		/// <param name="service">The container to export.</param>
		/// <param name="fqPath">A fqPath (with filename) to export to.</param>
		/// <param name="explode">
		///   If set to true, this will become the directory instead where the exploded tar file will reside,
		///   default is false.
		/// </param>
		/// <param name="throwOnError">If a exception shall be thrown if any error occurs.</param>
		/// <returns>The service itself.</returns>
		/// <exception cref="FluentDockerException">
		///   The exception thrown when an error occured and <paramref name="throwOnError" />
		///   is set to true.
		/// </exception>
		public static IContainerService Export(this IContainerService service, TemplateString fqPath, bool explode = false,
		  bool throwOnError = false)
		{
			var path = explode ? Path.GetTempFileName() : (string)fqPath;
			var res = service.DockerHost.Export(service.Id, path, service.Certificates);
			if (!res.Success)
			{
				if (throwOnError)
				{
					throw new FluentDockerException(
					  $"Failed to export {service.Id} to {fqPath} - result: {res}");
				}

				return service;
			}

			if (!explode)
			{
				return service;
			}

			try
			{
				path.UnTar(fqPath);
			}
			catch (Exception e)
			{
				if (throwOnError)
				{
					throw new FluentDockerException("Exception while untaring archive", e);
				}
			}
			finally
			{
				File.Delete(path);
			}

			return service;
		}

		/// <summary>
		///   Gets the running processes within the container.
		/// </summary>
		/// <param name="service">The container to get the processes from.</param>
		/// <returns>A <see cref="Processes" /> instance with one or more process rows.</returns>
		public static Processes GetRunningProcesses(this IContainerService service)
		{
			return service.DockerHost.Top(service.Id, service.Certificates).Data;
		}

		/// <summary>
		///   Copies file or directory from the <paramref name="containerPath" /> to the host <paramref name="hostPath" />.
		/// </summary>
		/// <param name="service">The container to copy from.</param>
		/// <param name="containerPath">The container path to copy from.</param>
		/// <param name="hostPath">The host path to copy to.</param>
		/// <param name="throwOnError">If it shall throw if any errors occur during copy.</param>
		/// <returns>The path where the files where copied to if successful, otherwise null is returned.</returns>
		/// <exception cref="FluentDockerException">If <paramref name="throwOnError" /> is true and error occured during copy.</exception>
		public static IContainerService CopyFrom(this IContainerService service, TemplateString containerPath,
		  TemplateString hostPath, bool throwOnError = false)
		{
			var res = service.DockerHost.CopyFromContainer(service.Id, containerPath, hostPath, service.Certificates);
			if (res.Success)
			{
				return service;
			}

			Debug.WriteLine($"Failed to copy from {service.Id}:{containerPath} to {hostPath} - result: {res}");
			if (throwOnError)
			{
				throw new FluentDockerException(
				  $"Failed to copy from {service.Id}:{containerPath} to {hostPath} - result: {res}");
			}

			return service;
		}

		/// <summary>
		///   Copies file or directory from the <paramref name="hostPath" /> to the containers <paramref name="containerPath" />.
		/// </summary>
		/// <param name="service">The container to copy to.</param>
		/// <param name="containerPath">The container path to copy to.</param>
		/// <param name="hostPath">The host path to copy from.</param>
		/// <param name="throwOnError">If it shall throw if any errors occur during copy.</param>
		/// <returns>The path where the files where copied from if successful, otherwise null is returned.</returns>
		/// <exception cref="FluentDockerException">If <paramref name="throwOnError" /> is true and error occured during copy.</exception>
		public static IContainerService CopyTo(this IContainerService service, TemplateString containerPath,
		  TemplateString hostPath, bool throwOnError = false)
		{
			var res = service.DockerHost.CopyToContainer(service.Id, containerPath, hostPath, service.Certificates);
			if (res.Success)
			{
				return service;
			}

			Debug.WriteLine($"Failed to copy to {service.Id}:{containerPath} from {hostPath} - result: {res}");
			if (throwOnError)
			{
				throw new FluentDockerException(
				  $"Failed to copy to {service.Id}:{containerPath} from {hostPath} - result: {res}");
			}

			return service;
		}

		/// <summary>
		///   Waits for process to start.
		/// </summary>
		/// <param name="service">The service to check processes within.</param>
		/// <param name="process">The process to wait for.</param>
		/// <param name="millisTimeout">Timeout giving up the wait.</param>
		/// <returns>The inparam service.</returns>
		public static IContainerService WaitForProcess(this IContainerService service, string process,
		  long millisTimeout = -1)
		{
			if (service == null)
				return null;

			Exception exception = null;
			var stopwatch = Stopwatch.StartNew();
			using (var mre = new ManualResetEventSlim())
			{
				Timer timer;
				using (timer = new Timer(_ =>
				{
					var processes = service.GetRunningProcesses();
					if (processes?.Rows.Any(x => x.Command == process) ?? false)
						mre.Set();
					if (stopwatch.ElapsedMilliseconds > millisTimeout)
					{
						exception = new FluentDockerException($"Wait expired for process {process} in container {service.Id}");
						mre.Set();
					}
				}, null, 0, 500))
					
					mre.Wait();
				timer.Dispose();
			}

			if (exception != null)
				throw exception;

			return service;
		}
		
		/// <summary>
		///   Waits for the container to be in a healthy state
		/// </summary>
		/// <param name="service">The service to check processes within.</param>
		/// <param name="millisTimeout">Timeout giving up the wait.</param>
		/// <returns>The inparam service.</returns>
		public static IContainerService WaitForHealthy(this IContainerService service, long millisTimeout = -1)
		{
			if (service == null)
				return null;

			Exception exception = null;
			var stopwatch = Stopwatch.StartNew();
			using (var mre = new ManualResetEventSlim())
			{
				Timer timer;
				using (timer = new Timer(_ =>
				{
					var config = service.GetConfiguration(true);
					if (config?.State?.Health?.Status == HealthState.Healthy)
						mre.Set();
					if (stopwatch.ElapsedMilliseconds > millisTimeout)
					{
						exception = new FluentDockerException($"Wait for healthy expired for container {service.Id}");
						mre.Set();
					}
				}, null, 0, 500))
					
					mre.Wait();
				timer.Dispose();
			}

			if (exception != null)
				throw exception;

			return service;
		}
		
		/// <summary>
		///   Waits for a specific message in the logs
		/// </summary>
		/// <param name="service">The service to check processes within.</param>
		/// <param name="message">The message to wait for</param>
		/// <param name="millisTimeout">Timeout giving up the wait.</param>
		/// <returns>The inparam service.</returns>
		public static IContainerService WaitForMessageInLogs(this IContainerService service, string message, long millisTimeout = -1)
		{
			if (service == null)
				return null;

			Exception exception = null;
			var stopwatch = Stopwatch.StartNew();
			using (var mre = new ManualResetEventSlim())
			{
				Timer timer;
				using (timer = new Timer(_ =>
				{
					var logs = service.Logs().Read();
					if (logs != null && logs.Contains(message))
						mre.Set();
					if (stopwatch.ElapsedMilliseconds > millisTimeout)
					{
						exception = new FluentDockerException($"Wait for message '{message}' in logs for container {service.Id}");
						mre.Set();
					}
				}, null, 0, 500))
					
				mre.Wait();
				timer.Dispose();
			}

			if (exception != null)
				throw exception;

			return service;
		}

		/// <summary>
		/// Waits using an arbitrary function.
		/// </summary>
		/// <param name="service">The service this lambda holds.</param>
		/// <param name="continuation">The lambda that do the custom action.</param>
		/// <returns>The service for fluent access.</returns>
		/// <exception cref="ArgumentNullException">If <paramref name="continuation"/> is null.</exception>
		/// <remarks>
		/// The lambda do the actual action to determine if the wait is over or not. If it returns zero or less, the
		/// wait is over. If it returns a positive value, the wait function will wait this amount of milliseconds before
		/// invoking it again. The second argument is the invocation count. This can be used for the function to determine
		/// any type of abort action due to the amount of invocations. If continuation wishes to abort, it shall throw
		/// <see cref="FluentDockerException"/>.
		/// </remarks>
		public static IContainerService Wait(this IContainerService service, Func<IContainerService, int, int> continuation)
		{
			if (null == continuation)
				throw new ArgumentNullException("Must specify a continuation", nameof(continuation));

			int wait;
			int count = 0;
			do
			{
				wait = continuation.Invoke(service, count++);
				if (wait > 0) Thread.Sleep(wait);
			} while (wait > 0);

			return service;
		}		
	}
}
