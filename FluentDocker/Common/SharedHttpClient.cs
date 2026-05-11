using System;
using System.Net.Http;

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
      return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    });

    /// <summary>
    /// Gets the shared <see cref="HttpClient"/> instance.
    /// Do not dispose this client. For longer timeouts, use per-request
    /// <see cref="System.Threading.CancellationTokenSource"/> instead.
    /// </summary>
    public static HttpClient Instance => s_instance.Value;
  }
}
