using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
      long millisTimeout = long.MaxValue, string address = null)
    {
      var endpoint = service.ToHostExposedEndpoint(portAndProto);

      if (!string.IsNullOrWhiteSpace(address))
      {
        if (null != endpoint)
          endpoint = new IPEndPoint(IPAddress.Parse(address), endpoint.Port);
      }

      if (endpoint == null)
        throw new FluentDockerException($"Can't find host endpoint for container port: {portAndProto}");

      endpoint.WaitForPort(millisTimeout);
      return service;
    }

    public static void WaitForPort(this IPEndPoint endpoint, long millisTimeout = long.MaxValue)
    {
      using (var s = new Socket(SocketType.Stream, ProtocolType.Tcp))
      {
        var waitMillis = (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond) + millisTimeout;
        while (true)
          try
          {
            s.Connect(endpoint);
            break;
          }
          catch (Exception ex)
          {
            Thread.Sleep(1000);
            var now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            
            if (now >= waitMillis)
              throw new FluentDockerException(
                $"Timeout waiting for service at = {endpoint.Address} port = {endpoint.Port}", ex);
          }
      }
    }

    /// <summary>
    ///   Invokes a HTTP request to url.
    /// </summary>
    /// <param name="service">The service to wait for.</param>
    /// <param name="url">The url including any query parameters.</param>
    /// <param name="timeout">
    ///   If no <paramref name="continuation" /> is specified, this is the timeout used. Default timeout is 60 seconds.
    /// </param>
    /// <param name="continuation">
    ///   Optional. The function to determine if to end the wait or wait a bit longer. When no function is set, it
    ///   default to check if it got 200 OK to continue. The second argument is the invocation count starting from zero.
    /// </param>
    /// <param name="method">Optional. The method. Default is <see cref="HttpMethod.Get" />.</param>
    /// <param name="contentType">Optional. The content type in put, post operations. Defaults to application/json</param>
    /// <param name="body">Optional. A body to post or put.</param>
    /// <returns>The response body in form of a string.</returns>
    /// <exception cref="FluentDockerException">When it fails to wait and wishes to terminate instead.</exception>
    /// <remarks>
    ///   The continuation function to get the <see cref="RequestResponse" /> as input and it is expected to output a
    ///   positive time in millisecond to wait until next HTTP request or zero if continuation. Negative return values are
    ///   also markers that wait is done. If function wishes to fail it uses <see cref="FluentDockerException" /> as its
    ///   signaling mechanism.
    /// </remarks>
    public static void WaitForHttp(this IContainerService service, string url, long timeout = 60_000,
      Func<RequestResponse, int, long> continuation = null, HttpMethod method = null,
      string contentType = "application/json", string body = null)
    {
      long wait = null == continuation ? timeout : 0;
      int count = 0;
      do
      {
        var time = Millis;

        var request = url.DoRequest(method, contentType, body).Result;
        if (null != continuation)
        {
          wait = continuation.Invoke(request, count++);
        }
        else
        {
          time = Millis - time;
          wait = request.Code != HttpStatusCode.OK ? wait - time : -1;
        }

        if (wait > 0) Thread.Sleep((int) wait);

      } while (wait > 0);
    }

    private static readonly DateTime Jan1St1970 = new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    /// <summary>Get extra long current timestamp</summary>
    private static long Millis => (long)((DateTime.UtcNow - Jan1St1970).TotalMilliseconds);

    /// <summary>
    ///   Translates a docker exposed port and protocol (on format 'port/proto' e.g. '534/tcp') to a
    ///   host endpoint that can be contacted outside the container.
    /// </summary>
    /// <param name="ports">The ports from the <see cref="ContainerNetworkSettings.Ports" /> property.</param>
    /// <param name="portAndProto">The port and protocol string.</param>
    /// <param name="dockerUri">Optional docker uri to use when the address is 0.0.0.0 in the endpoint.</param>
    /// <returns>A endpoint of the host exposed ip and port into the container port. If none is found, null is returned.</returns>
    public static IPEndPoint ToHostPort(this Dictionary<string, HostIpEndpoint[]> ports, string portAndProto,
      Uri dockerUri = null)
    {
      if (null == ports || string.IsNullOrEmpty(portAndProto)) return null;

      if (!ports.TryGetValue(portAndProto, out var endpoints)) return null;

      if (null == endpoints || endpoints.Length == 0) return null;

      if (CommandExtensions.IsNative()) return endpoints[0];

      if (CommandExtensions.IsEmulatedNative())
        return CommandExtensions.IsDockerDnsAvailable()
          ? new IPEndPoint(CommandExtensions.EmulatedNativeAdress(), endpoints[0].Port)
          : new IPEndPoint(IPAddress.Loopback, endpoints[0].Port);

      if (Equals(endpoints[0].Address, IPAddress.Any) && null != dockerUri)
        return new IPEndPoint(IPAddress.Parse(dockerUri.Host), endpoints[0].Port);

      return endpoints[0];
    }
  }
}