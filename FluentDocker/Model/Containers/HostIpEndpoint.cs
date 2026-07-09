#nullable enable
using System;
using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// Host-side endpoint binding emitted by Docker and Podman inspect JSON.
  /// </summary>
  public class HostIpEndpoint
  {
    private string? _hostIp;
    private string? _hostPort;

    /// <summary>Host IP address string. Empty means Docker bound on all interfaces.</summary>
    public string? HostIp
    {
      get => _hostIp;
      set => _hostIp = value;
    }

    /// <summary>Host TCP/UDP port string as emitted by Docker inspect.</summary>
    public string? HostPort
    {
      get => _hostPort;
      set => _hostPort = value;
    }

    /// <summary>
    /// Parsed host IP address. Empty bindings resolve to <see cref="IPAddress.Any"/>.
    /// Throws <see cref="InvalidOperationException"/> when <see cref="HostIp"/> is not a valid IP address;
    /// use <see cref="TryGetAddress"/> when inspecting partially-populated runtime DTOs.
    /// </summary>
    [JsonIgnore]
    public IPAddress Address => string.IsNullOrWhiteSpace(_hostIp) ? IPAddress.Any : ParseAddress(_hostIp);

    /// <summary>
    /// Parsed host port. Throws <see cref="InvalidOperationException"/> when <see cref="HostPort"/> is missing
    /// or invalid; use <see cref="TryGetPort"/> when inspecting partially-populated runtime DTOs.
    /// </summary>
    [JsonIgnore]
    public int Port => string.IsNullOrWhiteSpace(_hostPort)
        ? throw new InvalidOperationException("HostPort is not set.")
        : ParsePort(_hostPort);

    /// <summary>Attempts to parse <see cref="HostIp"/> without throwing.</summary>
    public bool TryGetAddress(out IPAddress? address)
    {
      if (string.IsNullOrWhiteSpace(_hostIp))
      {
        address = IPAddress.Any;
        return true;
      }

      return IPAddress.TryParse(_hostIp, out address);
    }

    /// <summary>Attempts to parse <see cref="HostPort"/> without throwing.</summary>
    public bool TryGetPort(out int port)
    {
      if (string.IsNullOrWhiteSpace(_hostPort))
      {
        port = 0;
        return false;
      }

      return int.TryParse(_hostPort, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
          port is >= IPEndPoint.MinPort and <= IPEndPoint.MaxPort;
    }

    private static IPAddress ParseAddress(string value)
    {
      if (IPAddress.TryParse(value, out var address))
        return address;
      throw new InvalidOperationException($"HostIp '{value}' is not a valid IP address.");
    }

    private static int ParsePort(string value)
    {
      if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
          port is >= IPEndPoint.MinPort and <= IPEndPoint.MaxPort)
        return port;
      throw new InvalidOperationException($"HostPort '{value}' is not a valid TCP/UDP port.");
    }
  }
}
