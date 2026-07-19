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
    public void Constructor_PlaintextHttpNonLoopbackWithApiKey_VerifyTlsFalse_StillThrows()
    {
      var endpoint = ModelRunnerEndpoint.Custom(new Uri("http://remote.example:12434"));

      var ex = Assert.Throws<ModelRunnerException>(() => new ModelApiConnection(
          endpoint, new ModelApiConnectionConfig { VerifyTls = false }, apiKey: "secret"));

      Assert.Equal(ErrorCodes.ModelInference.Unauthorized, ex.ErrorCode);
      Assert.Contains(nameof(ModelApiConnectionConfig.AllowApiKeyOverInsecureTransport), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Constructor_PlaintextHttpNonLoopbackWithApiKey_ExplicitInsecureOptIn_DoesNotThrow()
    {
      var endpoint = ModelRunnerEndpoint.Custom(new Uri("http://remote.example:12434"));

      await using var conn = new ModelApiConnection(
          endpoint,
          new ModelApiConnectionConfig { AllowApiKeyOverInsecureTransport = true },
          apiKey: "secret");
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
    public void Constructor_CertificatePathMissing_ThrowsClearException()
    {
      var missing = Path.Combine(OutCertRoot(), "missing-" + Guid.NewGuid().ToString("N"));
      var config = new ModelApiConnectionConfig { CertificatePath = missing, VerifyTls = false };
      var endpoint = ModelRunnerEndpoint.Custom(new Uri("https://localhost:12434"));

      var ex = Assert.Throws<InvalidOperationException>(() => new ModelApiConnection(endpoint, config));

      Assert.Contains("CertificatePath", ex.Message, StringComparison.Ordinal);
      Assert.Contains(missing, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "key.pem")]
    [InlineData(false, true, "cert.pem")]
    public void Constructor_CertificatePathWithIncompleteClientPair_Throws(
        bool includeCert, bool includeKey, string expectedMissing)
    {
      var dir = WritePemCertificates(includeCa: false, includeCert: includeCert, includeKey: includeKey);
      try
      {
        var config = new ModelApiConnectionConfig { CertificatePath = dir, VerifyTls = false };
        var endpoint = ModelRunnerEndpoint.Custom(new Uri("https://localhost:12434"));

        var ex = Assert.Throws<InvalidOperationException>(() => new ModelApiConnection(endpoint, config));

        Assert.Contains(expectedMissing, ex.Message, StringComparison.Ordinal);
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public async Task Constructor_ClientPairWithoutCa_UsesSystemTrust_DoesNotThrow()
    {
      // ca.pem is optional: a client-cert pair with system trust for the server is a
      // supported deployment shape (no exclusive pin without a ca.pem).
      var dir = WritePemCertificates(includeCa: false);
      try
      {
        var config = new ModelApiConnectionConfig { CertificatePath = dir, VerifyTls = true };
        var endpoint = ModelRunnerEndpoint.Custom(new Uri("https://localhost:12434"));

        await using var conn = new ModelApiConnection(endpoint, config);
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public void Constructor_CertificateDirWithNoPemFiles_ThrowsClearException()
    {
      // A configured-but-empty directory is a typo'd path, not a request for system trust.
      var dir = Path.Combine(OutCertRoot(), "empty-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      try
      {
        var config = new ModelApiConnectionConfig { CertificatePath = dir, VerifyTls = true };
        var endpoint = ModelRunnerEndpoint.Custom(new Uri("https://localhost:12434"));

        var ex = Assert.Throws<InvalidOperationException>(() => new ModelApiConnection(endpoint, config));

        Assert.Contains("none of", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ca.pem", ex.Message, StringComparison.Ordinal);
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public void Constructor_UnverifiedTlsToNonLoopbackWithApiKey_Throws()
    {
      // https with VerifyTls=false is MITM-equivalent transport: sending a bearer key to a
      // non-loopback host requires the same explicit opt-in as plaintext http.
      var endpoint = ModelRunnerEndpoint.Custom(new Uri("https://models.example.com:12434"));

      var ex = Assert.Throws<ModelRunnerException>(() => new ModelApiConnection(
          endpoint, new ModelApiConnectionConfig { VerifyTls = false }, apiKey: "secret"));

      Assert.Contains(nameof(ModelApiConnectionConfig.AllowApiKeyOverInsecureTransport), ex.Message, StringComparison.Ordinal);
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
    private static string WritePemCertificates(bool includeCa, bool includeCert = true, bool includeKey = true)
    {
      var dir = Path.Combine(OutCertRoot(), $"fd-modelconn-certs-{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);

      using var rsa = RSA.Create(2048);
      var request = new CertificateRequest(
          "CN=fluentdocker-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      using var cert = request.CreateSelfSigned(
          DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

      if (includeCert)
        File.WriteAllText(Path.Combine(dir, "cert.pem"), cert.ExportCertificatePem());
      if (includeKey)
        File.WriteAllText(Path.Combine(dir, "key.pem"), rsa.ExportPkcs8PrivateKeyPem());

      if (includeCa)
        File.WriteAllText(Path.Combine(dir, "ca.pem"), cert.ExportCertificatePem());

      return dir;
    }

    private static string OutCertRoot()
    {
      var root = Path.Combine(Directory.GetCurrentDirectory(), ".out", "test-certs");
      Directory.CreateDirectory(root);
      return root;
    }
  }
}
