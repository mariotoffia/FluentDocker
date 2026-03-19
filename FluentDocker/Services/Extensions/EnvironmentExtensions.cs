using System;
using System.Net;
using System.Net.Sockets;
using FluentDocker.Common;

namespace FluentDocker.Services.Extensions
{
  /// <summary>
  /// Environment detection extension methods for V3.
  /// </summary>
  public static class EnvironmentExtensions
  {
    private static IPAddress _cachedDockerIpAddress;

    /// <summary>
    /// Checks if running on native Linux Docker.
    /// </summary>
    public static bool IsNative()
    {
      return FdOs.IsLinux();
    }

    /// <summary>
    /// Checks if running on emulated native (Docker Desktop on Windows/Mac).
    /// </summary>
    public static bool IsEmulatedNative()
    {
      return !FdOs.IsLinux();
    }

    /// <summary>
    /// Checks if Docker DNS is available (host.docker.internal).
    /// </summary>
    public static bool IsDockerDnsAvailable()
    {
      try
      {
        Dns.GetHostEntry("host.docker.internal");
        return true;
      }
      catch (SocketException)
      {
        return false;
      }
    }

    /// <summary>
    /// Gets the Docker host address for containers to reach the host.
    /// </summary>
    /// <param name="useCache">Whether to cache the result.</param>
    /// <returns>The Docker host IP address.</returns>
    public static IPAddress GetDockerHostAddress(bool useCache = true)
    {
      if (useCache && _cachedDockerIpAddress != null)
        return _cachedDockerIpAddress;

      // On Linux, use host network or Docker's gateway
      if (FdOs.IsLinux())
      {
        // Docker gateway is typically 172.17.0.1 for bridge network
        // But for host access, use host.docker.internal if available
        if (IsDockerDnsAvailable())
        {
          var hostEntry = Dns.GetHostEntry("host.docker.internal");
          if (hostEntry.AddressList.Length > 0)
          {
            var v4Address = Array.Find(hostEntry.AddressList,
                x => x.AddressFamily == AddressFamily.InterNetwork);
            _cachedDockerIpAddress = v4Address ?? hostEntry.AddressList[^1];
            return _cachedDockerIpAddress;
          }
        }

        // Fallback to localhost
        _cachedDockerIpAddress = IPAddress.Loopback;
        return _cachedDockerIpAddress;
      }

      // On Windows/Mac (Docker Desktop), use host.docker.internal
      try
      {
        var hostEntry = Dns.GetHostEntry("host.docker.internal");
        if (hostEntry.AddressList.Length > 0)
        {
          // Prefer IPv4 addresses
          var v4Address = Array.Find(hostEntry.AddressList,
              x => x.AddressFamily == AddressFamily.InterNetwork);
          _cachedDockerIpAddress = v4Address ?? hostEntry.AddressList[^1];
        }
      }
      catch (SocketException)
      {
        // Fallback to localhost
        _cachedDockerIpAddress = IPAddress.Loopback;
      }

      return _cachedDockerIpAddress;
    }

    /// <summary>
    /// Gets the localhost address appropriate for the platform.
    /// </summary>
    public static string GetLocalhostAddress()
    {
      return FdOs.IsWindows() ? "localhost" : "127.0.0.1";
    }

    /// <summary>
    /// Checks if running in a Docker container.
    /// </summary>
    public static bool IsRunningInDocker()
    {
      // Check for .dockerenv file (Linux)
      if (System.IO.File.Exists("/.dockerenv"))
        return true;

      // Check for cgroup (Linux)
      try
      {
        var cgroup = System.IO.File.ReadAllText("/proc/1/cgroup");
        return cgroup.Contains("docker") || cgroup.Contains("kubepods");
      }
      catch (Exception ex)
      {
        Logger.Log($"Container environment check failed: {ex.Message}");
        return false;
      }
    }

    /// <summary>
    /// Gets the Docker socket path for the current platform.
    /// </summary>
    public static string GetDockerSocketPath()
    {
      if (FdOs.IsWindows())
        return @"//./pipe/docker_engine";

      return "/var/run/docker.sock";
    }

    /// <summary>
    /// Checks if Docker is using rootless mode.
    /// </summary>
    public static bool IsRootless()
    {
      // Check for XDG_RUNTIME_DIR-based socket
      var xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
      if (!string.IsNullOrEmpty(xdgRuntime))
      {
        var rootlessSocket = System.IO.Path.Combine(xdgRuntime, "docker.sock");
        if (System.IO.File.Exists(rootlessSocket))
          return true;
      }

      // Check for DOCKER_HOST pointing to rootless socket
      var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
      if (!string.IsNullOrEmpty(dockerHost) && dockerHost.Contains("rootless"))
        return true;

      return false;
    }
  }
}

