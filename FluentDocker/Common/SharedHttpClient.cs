#nullable enable
using System;
using System.Net.Http;
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

    /// <summary>
    /// Gets the shared <see cref="HttpClient"/> instance.
    /// Do not dispose this client. It has no global timeout; callers must
    /// enforce operation-specific limits with a per-request
    /// <see cref="System.Threading.CancellationToken"/>.
    /// </summary>
    public static HttpClient Instance => s_instance.Value;
  }
}
