using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Extensions
{
  public static class NetworkExtensions
  {
    public static void WaitForPort(this IPEndPoint endpoint, long millisTimeout = long.MaxValue)
    {
      WaitForPort(endpoint.Address.ToString(), endpoint.Port, millisTimeout);
    }

    public static void WaitForPort(this string host, int port, long millisTimeout = long.MaxValue)
    {
      using (var s = new Socket(SocketType.Stream, ProtocolType.Tcp))
      {
        long totalWait = 0;
        while (totalWait < millisTimeout)
        {
          try
          {
            s.Connect(host, port);
            break;
          }
          catch (Exception)
          {
            Thread.Sleep(1000);
            totalWait += 1000;
            if (totalWait >= millisTimeout)
            {
              throw new FluentDockerException($"Timeout waiting for port {port}");
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

      if (Equals(endpoints[0].Address, IPAddress.Any) && null != dockerUri)
      {
        return new IPEndPoint(IPAddress.Parse(dockerUri.Host), endpoints[0].Port);
      }

      return endpoints[0];
    }
  }
}