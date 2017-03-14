using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services.Extensions
{
	public static class NetworkExtensions
	{
		public static IContainerService WaitForPort(this IContainerService service, string portAndProto,
		  long millisTimeout = long.MaxValue)
		{
			service.ToHostExposedEndpoint(portAndProto).WaitForPort(millisTimeout);
			return service;
		}

		public static void WaitForPort(this IPEndPoint endpoint, long millisTimeout = long.MaxValue)
		{
			using (var s = new Socket(SocketType.Stream, ProtocolType.Tcp))
			{
				long totalWait = 0;
				while (totalWait < millisTimeout)
				{
					try
					{
						s.Connect(endpoint);
						break;
					}
					catch (Exception ex)
					{
						Thread.Sleep(1000);
						totalWait += 1000;
						if (totalWait >= millisTimeout)
						{
							throw new FluentDockerException($"Timeout waiting for service at = {endpoint.Address} port = {endpoint.Port}");
						}
					}
				}
			}
		}

		/// <summary>
		///   Translates a docker exposed port and protocol (on format 'port/proto' e.g. '534/tcp') to a
		///   host endpoint that can be contacted outside the container.
		/// </summary>
		/// <param name="ports">The ports from the <see cref="ContainerNetworkSettings.Ports" /> property.</param>
		/// <param name="portAndProto">The port and protocol string.</param>
		/// <param name="dockerUri">Optional docker uri to use when the adress is 0.0.0.0 in the endpoint.</param>
		/// <returns>A endpoint of the host exposed ip and port into the container port. If none is found, null is returned.</returns>
		public static IPEndPoint ToHostPort(this Dictionary<string, HostIpEndpoint[]> ports, string portAndProto,
		  Uri dockerUri = null)
		{
			if (null == ports || string.IsNullOrEmpty(portAndProto))
			{
				return null;
			}

			HostIpEndpoint[] endpoints;
			if (!ports.TryGetValue(portAndProto, out endpoints))
			{
				return null;
			}

			if (null == endpoints || endpoints.Length == 0)
			{
				return null;
			}

			if (CommandExtensions.IsNative())
			{
				return endpoints[0];
			}

			if (CommandExtensions.IsEmulatedNative())
			{
				if (CommandExtensions.IsDockerDnsAvailable())
				{
					return new IPEndPoint(CommandExtensions.EmulatedNativeAdress(), endpoints[0].Port);
				}
				return new IPEndPoint(IPAddress.Loopback, endpoints[0].Port);
			}

			if (Equals(endpoints[0].Address, IPAddress.Any) && null != dockerUri)
			{
				return new IPEndPoint(IPAddress.Parse(dockerUri.Host), endpoints[0].Port);
			}

			return endpoints[0];
		}
	}
}