using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentDocker.Drivers.Models.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  [Trait("Category", "Unit")]
  public sealed class ClientCertificateLoaderTests
  {
    [Fact]
    public void Load_ReturnsCertificateWithPrivateKey()
    {
      var directory = Path.Combine(".out", "ClientCertificateLoaderTests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);

      try
      {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=fluentdocker-test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var certPath = Path.Combine(directory, "cert.pem");
        var keyPath = Path.Combine(directory, "key.pem");
        File.WriteAllText(certPath, generated.ExportCertificatePem());
        File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());

        using var loaded = ClientCertificateLoader.Load(certPath, keyPath);

        Assert.NotNull(loaded);
        Assert.True(loaded.HasPrivateKey);
        Assert.Contains("fluentdocker-test", loaded.Subject, StringComparison.Ordinal);
      }
      finally
      {
        if (Directory.Exists(directory))
          Directory.Delete(directory, recursive: true);
      }
    }
  }
}
