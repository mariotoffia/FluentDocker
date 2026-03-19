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
    private string _apiVersion;
    private volatile bool _versionNegotiated;

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

      _apiVersion = config.ApiVersion;
    }

    public string ApiVersion => _apiVersion;

    public async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct);
      return await _httpClient.GetAsync(versionedPath, ct);
    }

    public async Task<HttpResponseMessage> PostAsync(
        string path, HttpContent content = null, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct);
      return await _httpClient.PostAsync(versionedPath, content, ct);
    }

    public async Task<HttpResponseMessage> PutAsync(
        string path, HttpContent content, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct);
      return await _httpClient.PutAsync(versionedPath, content, ct);
    }

    public async Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct);
      return await _httpClient.DeleteAsync(versionedPath, ct);
    }

    public async Task<Stream> GetStreamAsync(string path, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct);
      var response = await _httpClient.GetAsync(
          versionedPath, HttpCompletionOption.ResponseHeadersRead, ct);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadAsStreamAsync(ct);
    }

    public async Task<Stream> PostStreamAsync(
        string path, HttpContent content = null, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct);
      var request = new HttpRequestMessage(HttpMethod.Post, versionedPath) { Content = content };
      var response = await _httpClient.SendAsync(
          request, HttpCompletionOption.ResponseHeadersRead, ct);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadAsStreamAsync(ct);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
      try
      {
        var response = await _httpClient.GetAsync("/_ping", ct);
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
      if (!_versionNegotiated && string.IsNullOrEmpty(_apiVersion))
      {
        await _negotiationLock.WaitAsync(ct);
        try
        {
          // Double-check after acquiring the lock.
          if (!_versionNegotiated && string.IsNullOrEmpty(_apiVersion))
          {
            await NegotiateApiVersionAsync(ct);
          }
        }
        finally
        {
          _negotiationLock.Release();
        }
      }

      return string.IsNullOrEmpty(_apiVersion) ? path : $"/v{_apiVersion}{path}";
    }

    private async Task NegotiateApiVersionAsync(CancellationToken ct)
    {
      try
      {
        var response = await _httpClient.GetAsync("/_ping", ct);
        if (response.Headers.TryGetValues("API-Version", out var values))
        {
          foreach (var version in values)
          {
            _apiVersion = version;
            break;
          }
        }

        // Also check Docker-Experimental and OSType headers for diagnostics
        _versionNegotiated = true;
      }
      catch (Exception ex)
      {
        // If negotiation fails, proceed without version prefix
        Logger.Log($"Docker API version negotiation failed ({ex.Message}); proceeding without version prefix");
        _versionNegotiated = true;
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
          await socket.ConnectAsync(endpoint, ct);
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
          await pipe.ConnectAsync((int)config.ConnectionTimeout.TotalMilliseconds, ct);
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
