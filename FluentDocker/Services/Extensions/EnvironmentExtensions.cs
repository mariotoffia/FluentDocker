using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;

namespace FluentDocker.Services.Extensions
{
  /// <summary>
  /// Environment detection extension methods for V3.
  /// </summary>
  public static class EnvironmentExtensions
  {
    private static volatile IPAddress _cachedDockerIpAddress;
    private static DateTimeOffset _cachedDockerIpAddressExpiresAt;
    private static readonly object CacheLock = new();
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DockerHostAddressCacheTtl = TimeSpan.FromSeconds(30);

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
      return Task.Run(IsDockerDnsAvailableAsync).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Checks if Docker DNS is available (host.docker.internal).
    /// </summary>
    public static async Task<bool> IsDockerDnsAvailableAsync()
    {
      return await IsDockerDnsAvailableAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks if Docker DNS is available (host.docker.internal).
    /// </summary>
    public static async Task<bool> IsDockerDnsAvailableAsync(CancellationToken cancellationToken)
    {
      try
      {
        var addresses = await ResolveDockerDnsAsync(cancellationToken).ConfigureAwait(false);
        return addresses.Length > 0;
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        return false;
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (SocketException)
      {
        return false;
      }
    }

    private static async Task<IPAddress[]> ResolveDockerDnsAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeoutCts.CancelAfter(DnsTimeout);
      try
      {
        return await Dns.GetHostAddressesAsync("host.docker.internal", timeoutCts.Token)
            .ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        return [];
      }
    }

    private static async Task<IPAddress[]> TryResolveDockerDnsAsync(CancellationToken cancellationToken)
    {
      try
      {
        return await ResolveDockerDnsAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (SocketException)
      {
        return [];
      }
    }

    /// <summary>
    /// Gets the Docker host address for containers to reach the host.
    /// </summary>
    /// <param name="useCache">Whether to cache the result.</param>
    /// <returns>The Docker host IP address.</returns>
    public static IPAddress GetDockerHostAddress(bool useCache = true)
    {
      return Task.Run(() => GetDockerHostAddressAsync(useCache)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets the Docker host address for containers to reach the host.
    /// </summary>
    /// <param name="useCache">Whether to cache the result.</param>
    /// <returns>The Docker host IP address.</returns>
    public static async Task<IPAddress> GetDockerHostAddressAsync(bool useCache = true)
    {
      return await GetDockerHostAddressAsync(useCache, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the Docker host address for containers to reach the host.
    /// </summary>
    /// <param name="useCache">Whether to cache the result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Docker host IP address.</returns>
    /// <remarks>
    /// On Linux, when <c>host.docker.internal</c> cannot be resolved, this falls back to
    /// <c>172.17.0.1</c>, Docker's default bridge gateway convention. Custom bridge or rootless
    /// setups may need an explicit host override instead.
    /// </remarks>
    public static async Task<IPAddress> GetDockerHostAddressAsync(
        bool useCache,
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (useCache)
      {
        lock (CacheLock)
        {
          if (_cachedDockerIpAddress != null &&
              _cachedDockerIpAddressExpiresAt > DateTimeOffset.UtcNow)
          {
            return _cachedDockerIpAddress;
          }
        }
      }

      // On Linux, use host network or Docker's gateway
      if (FdOs.IsLinux())
      {
        // Docker gateway is typically 172.17.0.1 for bridge network, but for host access prefer
        // host.docker.internal when it resolves. Resolve exactly once (SVC-7): the previous
        // IsDockerDnsAvailableAsync pre-check performed a second, redundant DNS lookup, doubling
        // latency; branching on the resolved addresses gives the same result in a single lookup.
        var addresses = await TryResolveDockerDnsAsync(cancellationToken).ConfigureAwait(false);
        if (addresses.Length > 0)
        {
          var v4Address = Array.Find(addresses,
              x => x.AddressFamily == AddressFamily.InterNetwork);
          return CacheDockerHostAddress(v4Address ?? addresses[^1], useCache);
        }

        return CacheDockerHostAddress(IPAddress.Parse("172.17.0.1"), useCache);
      }

      // On Windows/Mac (Docker Desktop), use host.docker.internal
      var resolved = IPAddress.Loopback;
      try
      {
        var addresses = await TryResolveDockerDnsAsync(cancellationToken).ConfigureAwait(false);
        if (addresses.Length > 0)
        {
          // Prefer IPv4 addresses
          var v4Address = Array.Find(addresses,
              x => x.AddressFamily == AddressFamily.InterNetwork);
          resolved = v4Address ?? addresses[^1];
        }
      }
      catch (SocketException)
      {
      }

      return CacheDockerHostAddress(resolved, useCache);
    }

    private static IPAddress CacheDockerHostAddress(IPAddress address, bool useCache)
    {
      if (useCache)
      {
        lock (CacheLock)
        {
          _cachedDockerIpAddress = address;
          _cachedDockerIpAddressExpiresAt = DateTimeOffset.UtcNow.Add(DockerHostAddressCacheTtl);
        }
      }

      return address;
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
      // ponytail: cgroup v2 can be opaque; runtime marker files are the cheap reliable hint.
      if (System.IO.File.Exists("/run/.containerenv"))
        return true;

      // Check for cgroup (Linux)
      try
      {
        var cgroup = System.IO.File.ReadAllText("/proc/1/cgroup");
        return cgroup.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
            cgroup.Contains("kubepods", StringComparison.OrdinalIgnoreCase) ||
            cgroup.Contains("containerd", StringComparison.OrdinalIgnoreCase) ||
            cgroup.Contains("libpod", StringComparison.OrdinalIgnoreCase);
      }
      catch (Exception)
      {
        return false;
      }
    }

    /// <summary>
    /// Gets the Docker socket path for the current platform.
    /// </summary>
    /// <remarks>
    /// Resolution order: an explicit <c>DOCKER_HOST</c> unix/npipe endpoint wins; otherwise a
    /// rootless <c>XDG_RUNTIME_DIR/docker.sock</c> is used when present; otherwise the platform
    /// default (named pipe on Windows, <c>/var/run/docker.sock</c> elsewhere).
    /// </remarks>
    public static string GetDockerSocketPath()
    {
      var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
      if (!string.IsNullOrEmpty(dockerHost))
      {
        if (dockerHost.StartsWith("unix://", StringComparison.OrdinalIgnoreCase))
          return dockerHost.Substring("unix://".Length);
        if (dockerHost.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase))
          return dockerHost.Substring("npipe://".Length);
      }

      if (!FdOs.IsWindows())
      {
        var xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdgRuntime))
        {
          var rootlessSocket = System.IO.Path.Combine(xdgRuntime, "docker.sock");
          if (System.IO.File.Exists(rootlessSocket))
            return rootlessSocket;
        }
      }

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
