using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  public partial class DockerApiConnectionTests
  {
    [Fact]
    public async Task Constructor_NoExplicitHost_UsesDockerHostEnvironment()
    {
      var oldHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
      try
      {
        Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://env-docker.example:1234");
        await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
        {
          ApiVersion = "1.45"
        });

        var client = GetHttpClient(conn);

        Assert.Equal("http://env-docker.example:1234/", client.BaseAddress!.ToString());
      }
      finally
      {
        Environment.SetEnvironmentVariable("DOCKER_HOST", oldHost);
      }
    }

    [Fact]
    public async Task Constructor_ExplicitHost_WinsOverDockerHostEnvironment()
    {
      var oldHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
      try
      {
        Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://env-docker.example:1234");
        await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
        {
          Host = "tcp://explicit.example:2375",
          ApiVersion = "1.45"
        });

        var client = GetHttpClient(conn);

        Assert.Equal("http://explicit.example:2375/", client.BaseAddress!.ToString());
      }
      finally
      {
        Environment.SetEnvironmentVariable("DOCKER_HOST", oldHost);
      }
    }

    [Fact]
    public async Task Constructor_HonorsDockerCertPathAndTlsVerifyEnvironment()
    {
      var oldCertPath = Environment.GetEnvironmentVariable("DOCKER_CERT_PATH");
      var oldTlsVerify = Environment.GetEnvironmentVariable("DOCKER_TLS_VERIFY");
      var certPath = Path.GetFullPath(Path.Combine(
          ".out", "docker-api-certs", "env-" + Guid.NewGuid().ToString("N")));
      try
      {
        Directory.CreateDirectory(certPath);
        WriteCaCertificate(Path.Combine(certPath, "ca.pem"));
        Environment.SetEnvironmentVariable("DOCKER_CERT_PATH", certPath);
        // Docker convention: any non-empty DOCKER_TLS_VERIFY (even "0") ENABLES verification.
        // The environment must never silently disable server-certificate checks.
        Environment.SetEnvironmentVariable("DOCKER_TLS_VERIFY", "0");
        await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
        {
          Host = "tcp://env-docker.example:2376",
          ApiVersion = "1.45"
        });

        var config = GetConfig(conn);

        Assert.Equal(certPath, config.CertificatePath);
        Assert.True(config.VerifyTls);
      }
      finally
      {
        Environment.SetEnvironmentVariable("DOCKER_CERT_PATH", oldCertPath);
        Environment.SetEnvironmentVariable("DOCKER_TLS_VERIFY", oldTlsVerify);
        if (Directory.Exists(certPath))
          Directory.Delete(certPath, true);
      }
    }

    [Fact]
    public async Task Constructor_CertPathWithoutPort_DefaultsToTlsDockerPort()
    {
      var certPath = Path.GetFullPath(Path.Combine(
          ".out", "docker-api-certs", "cert-port-" + Guid.NewGuid().ToString("N")));
      try
      {
        Directory.CreateDirectory(certPath);
        WriteCaCertificate(Path.Combine(certPath, "ca.pem"));
        await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
        {
          Host = "tcp://cert-docker.example",
          CertificatePath = certPath,
          ApiVersion = "1.45"
        });

        var client = GetHttpClient(conn);

        Assert.Equal("https://cert-docker.example:2376/", client.BaseAddress!.ToString());
      }
      finally
      {
        if (Directory.Exists(certPath))
          Directory.Delete(certPath, true);
      }
    }

    [Fact]
    public async Task Constructor_DockerTlsVerifyEnvironment_NeverWeakensExplicitVerification()
    {
      var oldTlsVerify = Environment.GetEnvironmentVariable("DOCKER_TLS_VERIFY");
      try
      {
        // Even the literal "false" must not disable an explicit VerifyTls=true — a hostile or
        // stray env var cannot open the connection to a man-in-the-middle.
        Environment.SetEnvironmentVariable("DOCKER_TLS_VERIFY", "false");
        await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
        {
          Host = "tcp://env-docker.example:2376",
          ApiVersion = "1.45",
          VerifyTls = true
        });

        Assert.True(GetConfig(conn).VerifyTls);
      }
      finally
      {
        Environment.SetEnvironmentVariable("DOCKER_TLS_VERIFY", oldTlsVerify);
      }
    }

    private static HttpClient GetHttpClient(DockerApiConnection connection)
    {
      var field = typeof(DockerApiConnection).GetField(
          "_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(field);
      return (HttpClient)field.GetValue(connection)!;
    }

    private static DockerApiConnectionConfig GetConfig(DockerApiConnection connection)
    {
      var field = typeof(DockerApiConnection).GetField(
          "_config", BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(field);
      return (DockerApiConnectionConfig)field.GetValue(connection)!;
    }

    private static void WriteCaCertificate(string path)
    {
      using var key = RSA.Create(2048);
      var request = new CertificateRequest(
          "CN=fluentdocker-test-ca", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      using var certificate = request.CreateSelfSigned(
          DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
      File.WriteAllText(path, certificate.ExportCertificatePem());
    }
  }
}
