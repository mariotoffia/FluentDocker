#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentDocker.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>EXPOSE</c> instruction.</summary>
  public sealed class ExposeCommand : ICommand
  {
    /// <summary>Creates EXPOSE entries from numeric ports.</summary>
    /// <param name="ports">Ports to expose.</param>
    /// <exception cref="ArgumentException"><paramref name="ports"/> is null or empty (a bare <c>EXPOSE</c> is not a valid instruction).</exception>
    public ExposeCommand(params int[] ports)
      => Ports = RequirePorts(ports).Select(ValidatePort).ToArray();

    /// <summary>Creates EXPOSE entries from numeric ports, ranges, or port/protocol strings.</summary>
    /// <param name="ports">Ports to expose.</param>
    /// <exception cref="ArgumentException"><paramref name="ports"/> is null or empty (a bare <c>EXPOSE</c> is not a valid instruction).</exception>
    public ExposeCommand(params string[] ports)
      => Ports = RequirePorts(ports).Select(ValidatePort).ToArray();

    /// <summary>Gets the exposed ports.</summary>
    public IEnumerable<string> Ports { get; }

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      return $"EXPOSE {string.Join(" ", Ports)}";
    }

    private static T[] RequirePorts<T>(T[]? ports)
    {
      if (ports is null || ports.Length == 0)
        throw new ArgumentException(
            "EXPOSE requires at least one port; an empty port list would render an invalid Dockerfile instruction.",
            nameof(ports));
      return ports;
    }

    private static string ValidatePort(int port)
    {
      if (port < 1 || port > 65535)
        throw new FluentDockerException($"Invalid EXPOSE port '{port}'. Port must be 1-65535.");
      return port.ToString(CultureInfo.InvariantCulture);
    }

    private static string ValidatePort(string port)
    {
      DockerfileInstructionGuard.Validate(port ?? string.Empty, "EXPOSE", "port");
      var slash = port?.IndexOf('/') ?? -1;
      var portPart = slash >= 0 ? port![..slash] : port;
      if (!IsValidPortRange(portPart))
        throw new FluentDockerException($"Invalid EXPOSE port '{port}'. Port must be 1-65535.");
      if (slash >= 0 && !IsKnownProtocol(port![(slash + 1)..]))
        throw new FluentDockerException(
            $"Invalid EXPOSE port '{port}'. Protocol must be tcp, udp, or sctp.");
      return port!;
    }

    private static bool IsValidPortRange(string? value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return false;
      var parts = value.Split('-', 2);
      return IsValidPort(parts[0], out var start) &&
          (parts.Length == 1 || (IsValidPort(parts[1], out var end) && start <= end));
    }

    private static bool IsValidPort(string value, out int port) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
        port is >= 1 and <= 65535;

    private static bool IsKnownProtocol(string protocol) =>
        string.Equals(protocol, "tcp", System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(protocol, "udp", System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(protocol, "sctp", System.StringComparison.OrdinalIgnoreCase);
  }
}
