using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Containers;

namespace FluentDocker.Services.Extensions
{
  internal static class ServiceEndpointResolver
  {
    internal static async Task<IPEndPoint> ResolveAsync(
        IContainerService service,
        string portAndProto,
        Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> customResolver,
        Uri dockerHost,
        CancellationToken cancellationToken)
    {
      var config = await service.InspectAsync(cancellationToken).ConfigureAwait(false);
      return Resolve(config?.NetworkSettings?.Ports, portAndProto, customResolver, dockerHost);
    }

    internal static IPEndPoint Resolve(
        Dictionary<string, HostIpEndpoint[]> ports,
        string portAndProto,
        Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> customResolver,
        Uri dockerHost)
    {
      if (ports == null)
        return null;

      if (customResolver != null)
        return customResolver(ports, portAndProto, dockerHost);

      if (!ports.TryGetValue(portAndProto, out var bindings) ||
          bindings == null || bindings.Length == 0)
        return null;

      var binding = bindings.FirstOrDefault();
      if (binding == null || !int.TryParse(binding.HostPort, out var hostPort))
        return null;

      var hostIp = binding.HostIp;
      if (string.IsNullOrEmpty(hostIp) || hostIp == "0.0.0.0" || hostIp == "::")
        return new IPEndPoint(ResolveDockerHostAddress(dockerHost), hostPort);

      return new IPEndPoint(IPAddress.Parse(hostIp), hostPort);
    }

    internal static Uri GetDockerHostUri(string value)
    {
      return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static IPAddress ResolveDockerHostAddress(Uri dockerHost)
    {
      if (dockerHost == null ||
          dockerHost.Scheme is "unix" or "npipe" ||
          string.IsNullOrEmpty(dockerHost.Host))
        return IPAddress.Loopback;

      if (IPAddress.TryParse(dockerHost.Host, out var address))
        return address;

      return Dns.GetHostAddresses(dockerHost.Host).FirstOrDefault() ?? IPAddress.Loopback;
    }
  }
}
