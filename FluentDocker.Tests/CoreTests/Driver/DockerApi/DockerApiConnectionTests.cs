using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// <summary>Tests for DockerApiConnection and DockerApiConnectionConfig.</summary>
  [Trait("Category", "Unit")]
  public class DockerApiConnectionTests
  {
    #region GetDefaultHost

    [Fact]
    public void GetDefaultHost_OnCurrentPlatform_ReturnsExpectedScheme()
    {
      // GetDefaultHost is internal static, invoke via reflection
      var method = typeof(DockerApiConnection).GetMethod(
          "GetDefaultHost",
          BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
      Assert.NotNull(method);

      var result = (string)method.Invoke(null, null);
      Assert.NotNull(result);

      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        Assert.StartsWith("npipe:", result);
      else
        Assert.Equal("unix:///var/run/docker.sock", result);
    }

    #endregion

    #region Config Defaults

    [Fact]
    public void Config_DefaultValues_AreCorrect()
    {
      var config = new DockerApiConnectionConfig();

      Assert.Equal(TimeSpan.FromSeconds(30), config.ConnectionTimeout);
      Assert.Equal(TimeSpan.FromMinutes(5), config.RequestTimeout);
      Assert.True(config.VerifyTls);
      Assert.Null(config.ApiVersion);
      Assert.Null(config.Host);
      Assert.Null(config.CertificatePath);
    }

    [Fact]
    public void Config_CustomValues_AreRetained()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://remote:2375",
        CertificatePath = "/certs",
        VerifyTls = false,
        ConnectionTimeout = TimeSpan.FromSeconds(10),
        RequestTimeout = TimeSpan.FromMinutes(2),
        ApiVersion = "1.43"
      };

      Assert.Equal("tcp://remote:2375", config.Host);
      Assert.Equal("/certs", config.CertificatePath);
      Assert.False(config.VerifyTls);
      Assert.Equal(TimeSpan.FromSeconds(10), config.ConnectionTimeout);
      Assert.Equal(TimeSpan.FromMinutes(2), config.RequestTimeout);
      Assert.Equal("1.43", config.ApiVersion);
    }

    #endregion

    #region Constructor - Transport Selection

    [Fact]
    public async Task Constructor_UnixScheme_CreatesHandler()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "unix:///var/run/docker.sock",
        ApiVersion = "1.45"
      };

      await using var conn = new DockerApiConnection(config);
      Assert.NotNull(conn);
    }

    [Fact]
    public async Task Constructor_TcpScheme_CreatesHandler()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375",
        ApiVersion = "1.45"
      };

      await using var conn = new DockerApiConnection(config);
      Assert.NotNull(conn);
    }

    [Fact]
    public async Task Constructor_HttpsScheme_CreatesHandler()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "https://localhost:2376",
        VerifyTls = false,
        ApiVersion = "1.45"
      };

      await using var conn = new DockerApiConnection(config);
      Assert.NotNull(conn);
    }

    [Fact]
    public void Constructor_InvalidScheme_ThrowsArgumentException()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "ftp://localhost",
        ApiVersion = "1.45"
      };

      Assert.Throws<ArgumentException>(() => new DockerApiConnection(config));
    }

    #endregion

    #region PingAsync

    [Fact]
    public async Task PingAsync_WhenNoDockerRunning_ReturnsFalse()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:1",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = TimeSpan.FromSeconds(2)
      };

      await using var conn = new DockerApiConnection(config);
      var result = await conn.PingAsync(TestContext.Current.CancellationToken);

      Assert.False(result);
    }

    #endregion

    #region ApiVersion

    [Fact]
    public async Task ApiVersion_WhenSetInConfig_ReturnsConfigValue()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375",
        ApiVersion = "1.43"
      };

      await using var conn = new DockerApiConnection(config);
      Assert.Equal("1.43", conn.ApiVersion);
    }

    [Fact]
    public async Task ApiVersion_WhenNotSetInConfig_IsNull()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375"
      };

      await using var conn = new DockerApiConnection(config);
      Assert.Null(conn.ApiVersion);
    }

    #endregion

    #region Version Negotiation State

    [Fact]
    public async Task ApiVersion_WhenSetInConfig_SkipsNegotiation()
    {
      // When ApiVersion is pre-set, the negotiation state object should
      // reflect it immediately without needing a network call.
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375",
        ApiVersion = "1.45"
      };

      await using var conn = new DockerApiConnection(config);

      // The negotiation state should hold the configured version atomically.
      Assert.Equal("1.45", conn.ApiVersion);
      Assert.True(conn.IsVersionNegotiated);
    }

    [Fact]
    public async Task ApiVersion_WhenNotSetInConfig_NegotiationNotCompleted()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375"
      };

      await using var conn = new DockerApiConnection(config);

      // No version set and no negotiation yet
      Assert.Null(conn.ApiVersion);
      Assert.False(conn.IsVersionNegotiated);
    }

    #endregion

    #region DisposeAsync

    [Fact]
    public async Task DisposeAsync_CompletesWithoutError()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375",
        ApiVersion = "1.45"
      };

      var conn = new DockerApiConnection(config);
      await conn.DisposeAsync();
      // No exception means success
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
      // Disposing twice must not throw: _httpClient.Dispose() and X509Certificate2.Dispose()
      // are both no-ops on a second call.
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375",
        ApiVersion = "1.45"
      };

      var conn = new DockerApiConnection(config);

      await conn.DisposeAsync();
      await conn.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WithClientCertificate_DisposesCleanly()
    {
      // Exercises the TLS path that creates a client X509Certificate2 (cert.pem + key.pem):
      // construction succeeds, and disposing the connection (which now owns the cert) does
      // not throw and is idempotent.
      var dir = WritePemCertificates(includeCa: false);
      try
      {
        var config = new DockerApiConnectionConfig
        {
          Host = "https://localhost:2376",
          CertificatePath = dir,
          // Avoid needing a server handshake; we only assert construction + dispose are clean.
          VerifyTls = false,
          ApiVersion = "1.45"
        };

        var conn = new DockerApiConnection(config);

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
        var config = new DockerApiConnectionConfig
        {
          Host = "https://localhost:2376",
          CertificatePath = dir,
          VerifyTls = true,
          ApiVersion = "1.45"
        };

        var conn = new DockerApiConnection(config);

        await conn.DisposeAsync();
        await conn.DisposeAsync();
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    /// <summary>
    /// Writes a self-signed certificate as <c>cert.pem</c>/<c>key.pem</c> (and optionally a
    /// <c>ca.pem</c>) into a fresh temp directory and returns that directory's path.
    /// </summary>
    private static string WritePemCertificates(bool includeCa)
    {
      var dir = Path.Combine(Path.GetTempPath(), $"fd-dockerconn-certs-{Guid.NewGuid():N}");
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

    #endregion
  }
}
