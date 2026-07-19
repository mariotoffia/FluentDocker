#nullable disable warnings
using System;
using System.IO;

namespace FluentDocker.Drivers.Models.Connection
{
  public sealed partial class ModelApiConnection
  {
    private static (string CertPath, string KeyPath, string CaPath, bool HasClientCertificate)
        ValidateCertificatePath(ModelApiConnectionConfig config)
    {
      if (!Directory.Exists(config.CertificatePath))
        throw new InvalidOperationException(
            $"ModelApiConnectionConfig.CertificatePath '{config.CertificatePath}' does not exist.");

      var certPath = Path.Combine(config.CertificatePath, "cert.pem");
      var keyPath = Path.Combine(config.CertificatePath, "key.pem");
      var caPath = Path.Combine(config.CertificatePath, "ca.pem");
      var hasCert = File.Exists(certPath);
      var hasKey = File.Exists(keyPath);
      var hasCa = File.Exists(caPath);
      if (hasCert != hasKey)
        RequireFile(hasCert ? keyPath : certPath, hasCert ? "client private key" : "client certificate");
      if (!hasCert && !hasKey && !hasCa)
        throw new InvalidOperationException(
            $"Configured certificate directory '{config.CertificatePath}' contains none of " +
            "cert.pem, key.pem or ca.pem; check the path.");

      // ca.pem is optional: when present it becomes the exclusive trust root (pin); when
      // absent the system trust store validates the server certificate.
      return (certPath, keyPath, hasCa ? caPath : null, hasCert);
    }

    private static void RequireFile(string path, string description)
    {
      if (!File.Exists(path))
        throw new InvalidOperationException(
            $"Configured certificate directory is missing {Path.GetFileName(path)} ({description}).");
    }
  }
}
