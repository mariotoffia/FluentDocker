#nullable enable
using System;
using System.Net.Http;
using System.Net.Security;
using System.Threading;

namespace FluentDocker.Common
{
  /// <summary>
  /// Provides a shared, properly-configured <see cref="HttpClient"/> singleton
  /// for use across the library. Uses <see cref="SocketsHttpHandler"/> with
  /// pooled connection lifetime for DNS refresh.
  /// </summary>
  public static class SharedHttpClient
  {
    private static readonly Lazy<HttpClient> s_instance = new(() =>
    {
      var handler = new SocketsHttpHandler
      {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
      };
      return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    });

    private static readonly Lazy<HttpClient> s_insecureHttpsProbe = new(() =>
    {
      var handler = new SocketsHttpHandler
      {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        SslOptions = new SslClientAuthenticationOptions
        {
          // Readiness probing only: containers often serve self-signed certs on host IPs.
          // Intentionally accepts any certificate; this client is never used for data-plane
          // traffic, only to detect whether a local model runner is listening.
#pragma warning disable CA5359
          RemoteCertificateValidationCallback = static (_, _, _, _) => true
#pragma warning restore CA5359
        }
      };
      return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    });

    /// <summary>
    /// Gets the shared <see cref="HttpClient"/> instance.
    /// Do not dispose this client. It has no global timeout; callers must
    /// enforce operation-specific limits with a per-request
    /// <see cref="System.Threading.CancellationToken"/>.
    /// </summary>
    public static HttpClient Instance => s_instance.Value;

    /// <summary>
    /// Gets an HTTPS readiness-probe client that accepts self-signed and host-mismatched
    /// certificates. Do not use for authenticated application traffic.
    /// </summary>
    internal static HttpClient InsecureHttpsProbe => s_insecureHttpsProbe.Value;
  }
}
