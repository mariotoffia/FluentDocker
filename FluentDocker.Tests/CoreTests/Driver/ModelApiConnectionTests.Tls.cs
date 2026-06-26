using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>TLS / certificate and API-key transport-security tests for <see cref="ModelApiConnection"/>.</summary>
  public partial class ModelApiConnectionTests
  {
    [Fact]
    public async Task DisposeAsync_WithClientCertificate_DisposesCleanly()
    {
      // Exercises the TLS path that creates a client X509Certificate2 (cert.pem + key.pem):
      // construction succeeds, and disposing the connection (which now owns the cert) does
      // not throw and is idempotent.
      var dir = WritePemCertificates(includeCa: false);
      try
      {
        var config = new ModelApiConnectionConfig
        {
          CertificatePath = dir,
          // Avoid needing a server handshake; we only assert construction + dispose are clean.
          VerifyTls = false
        };

        var conn = new ModelApiConnection(
            ModelRunnerEndpoint.Custom(new Uri("https://localhost:12434")), config);

        await conn.DisposeAsync();
        await conn.DisposeAsync();
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public async Task DisposeAsync_WithClientAndCaCertificate_DisposesCleanly()
    {
      // Exercises both cert-creating branches: the client cert (cert.pem + key.pem) and the
      // custom CA (ca.pem) captured by the TLS validation callback. Both must be owned by the
      // connection and disposed cleanly (and idempotently) without throwing.
      var dir = WritePemCertificates(includeCa: true);
      try
      {
        var config = new ModelApiConnectionConfig
        {
          CertificatePath = dir,
          VerifyTls = true
        };

        var conn = new ModelApiConnection(
            ModelRunnerEndpoint.Custom(new Uri("https://localhost:12434")), config);

        await conn.DisposeAsync();
        await conn.DisposeAsync();
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public void Constructor_PlaintextHttpNonLoopbackWithApiKey_Throws()
    {
      // M1: sending a bearer token in cleartext to a non-loopback http:// host is refused by
      // default (VerifyTls=true) so the API key cannot leak over plaintext.
      var endpoint = ModelRunnerEndpoint.Custom(new Uri("http://remote.example:12434"));

      var ex = Assert.Throws<ModelRunnerException>(() => new ModelApiConnection(
          endpoint, new ModelApiConnectionConfig { VerifyTls = true }, apiKey: "secret"));

      Assert.Equal(ErrorCodes.ModelInference.Unauthorized, ex.ErrorCode);
      Assert.Contains("remote.example", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Constructor_PlaintextHttpNonLoopbackWithApiKey_VerifyTlsFalse_DoesNotThrow()
    {
      // M1: VerifyTls=false explicitly acknowledges the insecure transport, so the same
      // plaintext-bearer combination is allowed.
      var endpoint = ModelRunnerEndpoint.Custom(new Uri("http://remote.example:12434"));

      await using var conn = new ModelApiConnection(
          endpoint, new ModelApiConnectionConfig { VerifyTls = false }, apiKey: "secret");
    }

    [Fact]
    public async Task Constructor_HttpsNonLoopbackWithApiKey_DoesNotThrow()
    {
      // M1: an https endpoint encrypts the bearer token, so a non-loopback host with an API
      // key is allowed even with VerifyTls=true.
      var endpoint = ModelRunnerEndpoint.Custom(new Uri("https://remote.example:12434"));

      await using var conn = new ModelApiConnection(
          endpoint, new ModelApiConnectionConfig { VerifyTls = true }, apiKey: "secret");
    }

    [Fact]
    public async Task Constructor_PlaintextHttpLoopbackWithApiKey_DoesNotThrow()
    {
      // M1: a loopback http host is IsLoopback==true, so the bearer token never crosses the
      // network and the plaintext guard does not fire.
      var endpoint = ModelRunnerEndpoint.Custom(new Uri("http://127.0.0.1:12434"));

      await using var conn = new ModelApiConnection(
          endpoint, new ModelApiConnectionConfig { VerifyTls = true }, apiKey: "secret");
    }

    /// <summary>
    /// Writes a self-signed certificate as <c>cert.pem</c>/<c>key.pem</c> (and optionally a
    /// <c>ca.pem</c>) into a fresh temp directory and returns that directory's path.
    /// </summary>
    private static string WritePemCertificates(bool includeCa)
    {
      var dir = Path.Combine(Path.GetTempPath(), $"fd-modelconn-certs-{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);

      using var rsa = RSA.Create(2048);
      var request = new CertificateRequest(
          "CN=fluentdocker-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      using var cert = request.CreateSelfSigned(
          DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

      File.WriteAllText(Path.Combine(dir, "cert.pem"), cert.ExportCertificatePem());
      File.WriteAllText(Path.Combine(dir, "key.pem"), rsa.ExportPkcs8PrivateKeyPem());

      if (includeCa)
        File.WriteAllText(Path.Combine(dir, "ca.pem"), cert.ExportCertificatePem());

      return dir;
    }
  }
}
