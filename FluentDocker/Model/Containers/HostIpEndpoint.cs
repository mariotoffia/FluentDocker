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
    private string _hostIp;
    private string _hostPort;

    /// <summary>Host IP address string. Empty means Docker bound on all interfaces.</summary>
    public string HostIp
    {
      get => _hostIp;
      set
      {
        if (!string.IsNullOrWhiteSpace(value) && !IPAddress.TryParse(value, out _))
          throw new ArgumentException($"Invalid host IP address '{value}'.", nameof(value));

        _hostIp = value;
      }
    }

    /// <summary>Host TCP/UDP port string as emitted by Docker inspect.</summary>
    public string HostPort
    {
      get => _hostPort;
      set
      {
        if (!string.IsNullOrWhiteSpace(value) &&
            (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
             port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort))
          throw new ArgumentException($"Invalid host port '{value}'.", nameof(value));

        _hostPort = value;
      }
    }

    /// <summary>Parsed host IP address. Empty bindings resolve to <see cref="IPAddress.Any"/>.</summary>
    [JsonIgnore]
    public IPAddress Address => string.IsNullOrWhiteSpace(HostIp) ? IPAddress.Any : IPAddress.Parse(HostIp);

    /// <summary>Parsed host port.</summary>
    [JsonIgnore]
    public int Port => string.IsNullOrWhiteSpace(HostPort)
        ? throw new InvalidOperationException("HostPort is not set.")
        : int.Parse(HostPort, CultureInfo.InvariantCulture);
  }
}
