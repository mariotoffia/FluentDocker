using System;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  /// <summary>
  /// HTTP connection to the Docker Engine REST API.
  /// Supports Unix domain sockets, Windows named pipes, and TCP with optional TLS.
  /// </summary>
  public sealed class DockerApiConnection : IDockerApiConnection
  {
    private readonly HttpClient _httpClient;
    private readonly DockerApiConnectionConfig _config;
    private readonly SemaphoreSlim _negotiationLock = new(1, 1);

    /// <summary>
    /// Immutable record holding the negotiation result. A single volatile reference
    /// ensures both the API version and the negotiated flag are published atomically,
    /// preventing other threads from observing a partially-written state.
    /// </summary>
    private sealed record NegotiationState(string ApiVersion, bool Negotiated);

    private volatile NegotiationState _negotiation;

    public DockerApiConnection(DockerApiConnectionConfig config)
    {
      ArgumentNullException.ThrowIfNull(config);
      _config = config;

      var host = config.Host ?? GetDefaultHost();
      var (handler, baseAddress) = CreateHandler(host, config);

      _httpClient = new HttpClient(handler, disposeHandler: true)
      {
        BaseAddress = new Uri(baseAddress),
        Timeout = config.RequestTimeout
      };

      // If the user pre-set ApiVersion, mark negotiation as already done.
      _negotiation = !string.IsNullOrEmpty(config.ApiVersion)
          ? new NegotiationState(config.ApiVersion, Negotiated: true)
          : new NegotiationState(null, Negotiated: false);
    }

    public string ApiVersion => _negotiation.ApiVersion;

    /// <summary>
    /// Whether API version negotiation has completed (either via config or ping).
    /// </summary>
    public bool IsVersionNegotiated => _negotiation.Negotiated;

    public async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.GetAsync(versionedPath, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> PostAsync(
        string path, HttpContent content = null, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.PostAsync(versionedPath, content, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> PutAsync(
        string path, HttpContent content, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.PutAsync(versionedPath, content, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.DeleteAsync(versionedPath, ct).ConfigureAwait(false);
    }

    public async Task<Stream> GetStreamAsync(string path, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      var response = await _httpClient.GetAsync(
          versionedPath, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();
      var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
      return new ResponseOwningStream(stream, response);
    }

    public async Task<Stream> PostStreamAsync(
        string path, HttpContent content = null, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      var request = new HttpRequestMessage(HttpMethod.Post, versionedPath) { Content = content };
      var response = await _httpClient.SendAsync(
          request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();
      var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
      return new ResponseOwningStream(stream, response);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
      try
      {
        using var response = await _httpClient.GetAsync("/_ping", ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
      }
      catch (Exception ex)
      {
        Logger.Log($"Docker API ping failed: {ex.Message}");
        return false;
      }
    }

    public ValueTask DisposeAsync()
    {
      _negotiationLock.Dispose();
      _httpClient.Dispose();
      GC.SuppressFinalize(this);
      return ValueTask.CompletedTask;
    }

    private async Task<string> GetVersionedPathAsync(string path, CancellationToken ct)
    {
      var state = _negotiation;
      if (!state.Negotiated)
      {
        await _negotiationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
          // Double-check after acquiring the lock.
          state = _negotiation;
          if (!state.Negotiated)
          {
            await NegotiateApiVersionAsync(ct).ConfigureAwait(false);
            state = _negotiation;
          }
        }
        finally
        {
          _negotiationLock.Release();
        }
      }

      return string.IsNullOrEmpty(state.ApiVersion)
          ? path
          : $"/v{state.ApiVersion}{path}";
    }

    private async Task NegotiateApiVersionAsync(CancellationToken ct)
    {
      try
      {
        using var response = await _httpClient.GetAsync("/_ping", ct).ConfigureAwait(false);
        string version = null;
        if (response.Headers.TryGetValues("API-Version", out var values))
        {
          foreach (var v in values)
          {
            version = v;
            break;
          }
        }

        // Atomically publish both the version and the negotiated flag.
        _negotiation = new NegotiationState(version, Negotiated: true);
      }
      catch (Exception ex)
      {
        // If negotiation fails, proceed without version prefix
        Logger.Log($"Docker API version negotiation failed ({ex.Message}); proceeding without version prefix");
        _negotiation = new NegotiationState(null, Negotiated: true);
      }
    }

    internal static string GetDefaultHost()
    {
      return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
          ? "npipe:////./pipe/docker_engine"
          : "unix:///var/run/docker.sock";
    }

    private static (SocketsHttpHandler handler, string baseAddress) CreateHandler(
        string host, DockerApiConnectionConfig config)
    {
      var uri = new Uri(host);

      return uri.Scheme.ToLowerInvariant() switch
      {
        "unix" => CreateUnixSocketHandler(uri, config),
        "npipe" => CreateNamedPipeHandler(uri, config),
        "tcp" or "http" => CreateTcpHandler(uri, config, useTls: false),
        "https" => CreateTcpHandler(uri, config, useTls: true),
        _ => throw new ArgumentException($"Unsupported URI scheme: {uri.Scheme}. " +
            "Use unix://, npipe://, tcp://, or https://", nameof(host))
      };
    }

    private static (SocketsHttpHandler, string) CreateUnixSocketHandler(
        Uri uri, DockerApiConnectionConfig config)
    {
      var socketPath = uri.AbsolutePath;
      var handler = new SocketsHttpHandler
      {
        ConnectCallback = async (context, ct) =>
        {
          var socket = new Socket(
                      AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
          var endpoint = new UnixDomainSocketEndPoint(socketPath);
          await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);
          return new NetworkStream(socket, ownsSocket: true);
        },
        ConnectTimeout = config.ConnectionTimeout
      };

      return (handler, "http://localhost");
    }

    private static (SocketsHttpHandler, string) CreateNamedPipeHandler(
        Uri uri, DockerApiConnectionConfig config)
    {
      var pipeName = uri.AbsolutePath.TrimStart('/');
      var handler = new SocketsHttpHandler
      {
        ConnectCallback = async (_, ct) =>
        {
          var pipe = new NamedPipeClientStream(
                      ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
          await pipe.ConnectAsync((int)config.ConnectionTimeout.TotalMilliseconds, ct).ConfigureAwait(false);
          return pipe;
        },
        ConnectTimeout = config.ConnectionTimeout
      };

      return (handler, "http://localhost");
    }

    private static (SocketsHttpHandler, string) CreateTcpHandler(
        Uri uri, DockerApiConnectionConfig config, bool useTls)
    {
      var handler = new SocketsHttpHandler
      {
        ConnectTimeout = config.ConnectionTimeout
      };

      var hasCerts = !string.IsNullOrEmpty(config.CertificatePath);

      if (useTls || hasCerts)
      {
        var sslOptions = new SslClientAuthenticationOptions();

        if (hasCerts)
        {
          var certPath = Path.Combine(config.CertificatePath, "cert.pem");
          var keyPath = Path.Combine(config.CertificatePath, "key.pem");

          if (File.Exists(certPath) && File.Exists(keyPath))
          {
            var clientCert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            sslOptions.ClientCertificates = new X509CertificateCollection { clientCert };
          }

          if (!config.VerifyTls)
          {
#pragma warning disable CA5359 // Intentional: user opted out of TLS verification via VerifyTls=false
            sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
          }
          else
          {
            var caPath = Path.Combine(config.CertificatePath, "ca.pem");
            if (File.Exists(caPath))
            {
#if NET9_0_OR_GREATER
              var caCert = X509CertificateLoader.LoadCertificateFromFile(caPath);
#else
              var caCert = X509Certificate2.CreateFromPemFile(caPath);
#endif
              sslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
              {
                if (errors == SslPolicyErrors.None)
                  return true;
                if (chain == null || cert == null)
                  return false;
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(caCert);
                return chain.Build(new X509Certificate2(cert));
              };
            }
          }
        }
        else if (!config.VerifyTls)
        {
#pragma warning disable CA5359 // Intentional: user opted out of TLS verification via VerifyTls=false
          sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
        }

        handler.SslOptions = sslOptions;
      }

      var scheme = (useTls || hasCerts) ? "https" : "http";
      var port = uri.Port > 0 ? uri.Port : (useTls ? 2376 : 2375);
      var baseAddress = $"{scheme}://{uri.Host}:{port}";

      return (handler, baseAddress);
    }
  }
}
