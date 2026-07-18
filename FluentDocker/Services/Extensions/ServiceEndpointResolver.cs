using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Containers;

namespace FluentDocker.Services.Extensions
{
  internal static class ServiceEndpointResolver
  {
    private static readonly TimeSpan DockerHostAddressCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, (IPAddress Address, DateTimeOffset ExpiresAt)> DockerHostAddressCache = new();

    internal static async Task<IPEndPoint> ResolveAsync(
        IContainerService service,
        string portAndProto,
        Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> customResolver,
        Uri dockerHost,
        CancellationToken cancellationToken)
    {
      var config = await service.InspectAsync(cancellationToken).ConfigureAwait(false);
      return await ResolveAsync(
          config?.NetworkSettings?.Ports,
          portAndProto,
          customResolver,
          dockerHost,
          cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IPEndPoint> ResolveAsync(
        Dictionary<string, HostIpEndpoint[]> ports,
        string portAndProto,
        Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> customResolver,
        Uri dockerHost,
        CancellationToken cancellationToken)
    {
      // The custom resolver is consulted even when the inspect carries no port map (ports == null):
      // resolvers exist precisely to handle host-network/portless containers that the default
      // binding-based logic below cannot resolve.
      if (customResolver != null)
        return customResolver(ports, portAndProto, dockerHost);

      if (ports == null)
        return null;

      if (!ports.TryGetValue(portAndProto, out var bindings) ||
          bindings == null || bindings.Length == 0)
        return null;

      foreach (var binding in bindings)
      {
        if (binding == null || !int.TryParse(binding.HostPort, out var hostPort))
          continue;
        if (hostPort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
          continue;

        var hostIp = binding.HostIp;
        if (string.IsNullOrEmpty(hostIp) || hostIp == "0.0.0.0" || hostIp == "::")
        {
          return new IPEndPoint(
              await ResolveDockerHostAddressAsync(dockerHost, cancellationToken).ConfigureAwait(false),
              hostPort);
        }

        if (IPAddress.TryParse(hostIp, out var address))
        {
          // A literal loopback binding (127.0.0.1/::1) only means "this daemon's own loopback".
          // Returning it verbatim for a REMOTE daemon would make the caller probe its own
          // loopback instead of the daemon's — mirror the wildcard branch and resolve to the
          // daemon host instead (still an honest failure/timeout if the port is daemon-local-only,
          // rather than a false-positive against the wrong machine).
          if (IPAddress.IsLoopback(address) && IsRemoteDaemon(dockerHost))
          {
            return new IPEndPoint(
                await ResolveDockerHostAddressAsync(dockerHost, cancellationToken).ConfigureAwait(false),
                hostPort);
          }

          return new IPEndPoint(address, hostPort);
        }
      }

      return null;
    }

    internal static Uri GetDockerHostUri(string value)
    {
      return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static bool IsRemoteDaemon(Uri dockerHost)
    {
      if (dockerHost == null || dockerHost.Scheme is not ("tcp" or "ssh") || string.IsNullOrEmpty(dockerHost.Host))
        return false;
      if (string.Equals(dockerHost.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        return false;

      return !IPAddress.TryParse(dockerHost.Host, out var hostAddress) || !IPAddress.IsLoopback(hostAddress);
    }

    private static async Task<IPAddress> ResolveDockerHostAddressAsync(
        Uri dockerHost,
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (dockerHost == null ||
          dockerHost.Scheme is "unix" or "npipe" ||
          string.IsNullOrEmpty(dockerHost.Host))
        return IPAddress.Loopback;

      if (IPAddress.TryParse(dockerHost.Host, out var address))
        return address;

      if (DockerHostAddressCache.TryGetValue(dockerHost.Host, out var cached) &&
          cached.ExpiresAt > DateTimeOffset.UtcNow)
      {
        return cached.Address;
      }

      var resolved = await ResolveHostAsync(dockerHost.Host, cancellationToken).ConfigureAwait(false);
      DockerHostAddressCache[dockerHost.Host] = (resolved, DateTimeOffset.UtcNow.Add(DockerHostAddressCacheTtl));
      return resolved;
    }

    private static async Task<IPAddress> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
      var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
      return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork) ??
          addresses.FirstOrDefault() ??
          throw new InvalidOperationException($"Docker host '{host}' resolved without addresses.");
    }
  }
}
