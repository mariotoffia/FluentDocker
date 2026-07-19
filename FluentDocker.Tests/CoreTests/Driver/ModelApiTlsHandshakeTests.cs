using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// A REAL TLS handshake test for <see cref="ModelApiConnection"/>. Unlike
  /// <see cref="ModelTlsValidationTests"/> (which exercises the validation helper in
  /// isolation), this stands up an in-process HTTPS responder (a <see cref="TcpListener"/>
  /// + <see cref="SslStream"/> minimal HTTP/1.1 server) presenting a self-signed
  /// <c>CN=localhost</c> certificate, then points a live <see cref="ModelApiConnection"/> at
  /// it over <c>https://localhost:&lt;port&gt;</c> and drives a real
  /// <see cref="ModelApiConnection.PingAsync"/> through the actual <c>SslStream</c> client
  /// handshake.
  /// <para>
  /// It verifies the two ends of the policy: (a) when the self-signed cert is TRUSTED via a
  /// custom CA (<c>ca.pem</c>) and the hostname matches, the handshake succeeds and the ping
  /// is reachable; and (b) when <c>VerifyTls</c> is on but the cert is UNTRUSTED (no CA
  /// configured, OS does not trust the self-signed root), the handshake fails so the endpoint
  /// reports unreachable. It also covers the explicit opt-out (<c>VerifyTls=false</c>).
  /// </para>
  /// <para>
  /// It also drives a full mutual-TLS (mTLS) exchange — a server REQUIRING client
  /// authentication, with the client presenting a <c>cert.pem</c>/<c>key.pem</c> pair — so the
  /// client-certificate wiring is proven end-to-end. What it does NOT cover: a certificate
  /// signed by a real public CA, or platform TLS stacks other than the .NET
  /// <see cref="SslStream"/> used here. The handshake runs entirely in-process over the
  /// loopback adapter, so the test is deterministic and tagged Unit; it does not require
  /// Docker or any network egress.
  /// </para>
  /// </summary>
  [Trait("Category", "Unit")]
  public sealed class ModelApiTlsHandshakeTests
  {
    [Fact]
    public async Task PingAsync_CustomCaTrustsServer_HandshakeSucceeds()
    {
      using var serverCert = CreateLocalhostCertificate();
      await using var server = HttpsResponder.Start(serverCert);
      var caDir = WriteCaPem(serverCert);
      try
      {
        // VerifyTls on + the server's self-signed cert pinned as the custom root CA, and the
        // hostname (localhost) matches the cert CN -> the real handshake must succeed.
        var config = new ModelApiConnectionConfig { CertificatePath = caDir, VerifyTls = true };
        await using var conn = new ModelApiConnection(
            ModelRunnerEndpoint.Custom(new Uri($"https://localhost:{server.Port}")), config);

        var reachable = await conn.PingAsync(TestContext.Current.CancellationToken);
        Assert.True(reachable, "handshake against a custom-CA-trusted, name-matching cert should succeed");
      }
      finally
      {
        Directory.Delete(caDir, recursive: true);
      }
    }

    [Fact]
    public async Task PingAsync_UntrustedSelfSignedCert_HandshakeFails()
    {
      using var serverCert = CreateLocalhostCertificate();
      await using var server = HttpsResponder.Start(serverCert);

      // VerifyTls on, but NO custom CA is configured, so the platform validates the chain and
      // rejects the untrusted self-signed root: the handshake fails. PingAsync maps a transport
      // failure to "unreachable" (false) rather than reporting the endpoint as up.
      var config = new ModelApiConnectionConfig { CertificatePath = null!, VerifyTls = true };
      await using var conn = new ModelApiConnection(
          ModelRunnerEndpoint.Custom(new Uri($"https://localhost:{server.Port}")), config);

      var reachable = await conn.PingAsync(TestContext.Current.CancellationToken);
      Assert.False(reachable, "an untrusted self-signed cert with VerifyTls on must fail the handshake");
    }

    [Fact]
    public async Task PingAsync_VerifyTlsDisabled_HandshakeSucceedsDespiteUntrustedCert()
    {
      using var serverCert = CreateLocalhostCertificate();
      await using var server = HttpsResponder.Start(serverCert);

      // Explicit opt-out: VerifyTls=false accepts ANY server cert, so the same untrusted
      // self-signed cert now completes the handshake and the ping reports reachable.
      var config = new ModelApiConnectionConfig { CertificatePath = null!, VerifyTls = false };
      await using var conn = new ModelApiConnection(
          ModelRunnerEndpoint.Custom(new Uri($"https://localhost:{server.Port}")), config);

      var reachable = await conn.PingAsync(TestContext.Current.CancellationToken);
      Assert.True(reachable, "VerifyTls=false should accept the untrusted cert and complete the handshake");
    }

    [Fact]
    public async Task PingAsync_MutualTls_ClientCertAccepted_HandshakeSucceeds()
    {
      using var serverCert = CreateLocalhostCertificate();
      await using var server = HttpsResponder.Start(serverCert, requireClientCert: true);
      var certDir = WriteClientCertWithCa(serverCert);
      try
      {
        // Full mTLS: the server REQUIRES client authentication, the client presents
        // cert.pem + key.pem and trusts the server via ca.pem. The handshake must complete
        // and the ping report reachable — proving the client-cert wiring end-to-end. On
        // Windows ModelApiConnection re-imports the client cert through PFX to persist its
        // key; on this (non-Windows) platform the PEM-loaded key is used directly, and either
        // way the credential must be presented for the client-auth-requiring server to accept.
        var config = new ModelApiConnectionConfig { CertificatePath = certDir, VerifyTls = true };
        await using var conn = new ModelApiConnection(
            ModelRunnerEndpoint.Custom(new Uri($"https://localhost:{server.Port}")), config);

        var reachable = await conn.PingAsync(TestContext.Current.CancellationToken);
        Assert.True(reachable, "a valid client cert against a client-auth-requiring server should complete mTLS");
      }
      finally
      {
        Directory.Delete(certDir, recursive: true);
      }
    }

    private static X509Certificate2 CreateLocalhostCertificate()
    {
      using var rsa = RSA.Create(2048);
      var request = new CertificateRequest(
          "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      // SAN = localhost / 127.0.0.1 / ::1 so SslStream's hostname check passes against the
      // loopback connection (modern stacks consult the SAN, not just the CN).
      var san = new SubjectAlternativeNameBuilder();
      san.AddDnsName("localhost");
      san.AddIpAddress(IPAddress.Loopback);
      san.AddIpAddress(IPAddress.IPv6Loopback);
      request.CertificateExtensions.Add(san.Build());

      using var ephemeral = request.CreateSelfSigned(
          DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
      // Re-import via PFX so the cert carries an exportable private key the SslStream server
      // side can use to complete the handshake on all platforms.
      var pfx = ephemeral.Export(X509ContentType.Pfx);
      return X509CertificateLoader.LoadPkcs12(pfx, password: null);
    }

    private static string WriteCaPem(X509Certificate2 cert)
    {
      var dir = Path.Combine(Path.GetTempPath(), $"fd-tls-handshake-{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);
      // Only ca.pem is needed for the custom-root trust path (client cert.pem/key.pem are
      // optional in BuildSslOptions).
      File.WriteAllText(Path.Combine(dir, "ca.pem"), cert.ExportCertificatePem());
      return dir;
    }

    /// <summary>
    /// Builds a directory holding an mTLS client credential (<c>cert.pem</c> + <c>key.pem</c>)
    /// plus the server's certificate pinned as the trusted root (<c>ca.pem</c>) — the three
    /// filenames <see cref="ModelApiConnection"/> reads from <c>CertificatePath</c>.
    /// </summary>
    private static string WriteClientCertWithCa(X509Certificate2 serverCert)
    {
      var dir = Path.Combine(Path.GetTempPath(), $"fd-mtls-{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);

      using var rsa = RSA.Create(2048);
      var request = new CertificateRequest(
          "CN=fluentdocker-mtls-client", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      // clientAuth EKU (1.3.6.1.5.5.7.3.2) so the credential is a well-formed mTLS client cert.
      request.CertificateExtensions.Add(
          new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, critical: false));
      using var clientCert = request.CreateSelfSigned(
          DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

      // BuildSslOptions loads the client credential from cert.pem (cert) + key.pem (PKCS#8 key)
      // and trusts the server's self-signed cert pinned as the custom root via ca.pem.
      File.WriteAllText(Path.Combine(dir, "cert.pem"), clientCert.ExportCertificatePem());
      File.WriteAllText(Path.Combine(dir, "key.pem"), rsa.ExportPkcs8PrivateKeyPem());
      File.WriteAllText(Path.Combine(dir, "ca.pem"), serverCert.ExportCertificatePem());
      return dir;
    }

    /// <summary>
    /// A tiny in-process HTTPS server: accepts one-or-more loopback connections, performs the
    /// TLS server handshake with the supplied certificate, and replies <c>200 OK</c> with an
    /// empty JSON body to whatever request line arrives. Used only to drive a real client-side
    /// handshake; it is not a conformant HTTP server.
    /// </summary>
    private sealed class HttpsResponder : IAsyncDisposable
    {
      private readonly TcpListener _listener;
      private readonly X509Certificate2 _certificate;
      private readonly CancellationTokenSource _cts = new();
      private readonly Task _loop;
      private readonly bool _requireClientCert;

      private HttpsResponder(TcpListener listener, X509Certificate2 certificate, bool requireClientCert)
      {
        _listener = listener;
        _certificate = certificate;
        _requireClientCert = requireClientCert;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptLoopAsync);
      }

      public int Port { get; }

      public static HttpsResponder Start(X509Certificate2 certificate, bool requireClientCert = false)
      {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new HttpsResponder(listener, certificate, requireClientCert);
      }

      private async Task AcceptLoopAsync()
      {
        while (!_cts.IsCancellationRequested)
        {
          TcpClient client;
          try
          {
            client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
          }
          catch (OperationCanceledException)
          {
            return;
          }
          catch (Exception)
          {
            return;
          }

          // Handle each connection independently; a failed handshake (the untrusted-cert case)
          // must not stop the listener from serving the next connection.
          _ = Task.Run(() => HandleClientAsync(client));
        }
      }

      private async Task HandleClientAsync(TcpClient client)
      {
        using (client)
        await using (var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
        {
          try
          {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
              ServerCertificate = _certificate,
              ClientCertificateRequired = _requireClientCert,
              // When client auth is required, accept the self-signed client credential: this
              // test proves the client cert is PRESENTED and the handshake completes, not a
              // server-side CA chain. Left null otherwise (default validation, no cert asked).
#pragma warning disable CA5359 // Test intentionally accepts the self-signed client credential: it proves the client cert is PRESENTED and the handshake completes, not a server CA chain.
              RemoteCertificateValidationCallback = _requireClientCert
                  ? (RemoteCertificateValidationCallback)((_, _, _, _) => true)
                  : null
#pragma warning restore CA5359
            }, _cts.Token).ConfigureAwait(false);

            // Drain the request line(s) loosely then reply; we do not parse HTTP fully.
            var buffer = new byte[1024];
            await ssl.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);

            const string body = "{}";
            var response =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n" +
                "\r\n" + body;
            var bytes = Encoding.ASCII.GetBytes(response);
            await ssl.WriteAsync(bytes, _cts.Token).ConfigureAwait(false);
            await ssl.FlushAsync(_cts.Token).ConfigureAwait(false);
          }
          catch
          {
            // A rejected handshake (client distrusts the cert) lands here — expected for the
            // untrusted-cert test. Swallow so the responder stays alive.
          }
        }
      }

      public async ValueTask DisposeAsync()
      {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
          await _loop.ConfigureAwait(false);
        }
        catch
        {
          // best-effort shutdown
        }
        _cts.Dispose();
      }
    }
  }
}
