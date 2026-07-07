using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiConnectionCertificateValidationTests
  {
    [Fact]
    public void Constructor_WhenCertificateDirectoryDoesNotExist_Throws()
    {
      var path = Path.GetFullPath(Path.Combine(
          ".out", "docker-api-certs", "missing-" + Guid.NewGuid().ToString("N")));

      var error = Assert.Throws<InvalidOperationException>(() => new DockerApiConnection(
          new DockerApiConnectionConfig
          {
            Host = "tcp://localhost:2376",
            CertificatePath = path
          }));

      Assert.Contains($"DockerApiConnectionConfig.CertificatePath '{path}' does not exist.", error.Message);
    }

    [Fact]
    public void Constructor_WhenCertPemExistsWithoutKeyPem_ThrowsMissingKey()
    {
      var path = CreateOutDirectory("cert-only");
      try
      {
        File.WriteAllText(Path.Combine(path, "cert.pem"), "not used before validation");

        var error = Assert.Throws<InvalidOperationException>(() => new DockerApiConnection(
            new DockerApiConnectionConfig
            {
              Host = "tcp://localhost:2376",
              CertificatePath = path
            }));

        Assert.Contains("missing key.pem (client private key)", error.Message);
      }
      finally
      {
        Directory.Delete(path, true);
      }
    }

    [Fact]
    public async Task Constructor_WhenOnlyCaPemExists_DoesNotThrow()
    {
      var path = CreateOutDirectory("ca-only");
      try
      {
        WriteCaCertificate(Path.Combine(path, "ca.pem"));

        await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
        {
          Host = "tcp://localhost:2376",
          CertificatePath = path
        });

        Assert.NotNull(connection);
      }
      finally
      {
        Directory.Delete(path, true);
      }
    }

    private static string CreateOutDirectory(string name)
    {
      var path = Path.GetFullPath(Path.Combine(
          ".out", "docker-api-certs", name, Guid.NewGuid().ToString("N")));
      Directory.CreateDirectory(path);
      return path;
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
