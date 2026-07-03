using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace FluentDocker.Drivers.Models.Connection
{
  public sealed partial class ModelApiConnection
  {
    /// <summary>
    /// Loads a client (mTLS) certificate from a PEM <c>cert.pem</c> / <c>key.pem</c> pair.
    /// <para>
    /// On non-Windows platforms the certificate produced by
    /// <see cref="X509Certificate2.CreateFromPemFile(string, string)"/> is returned unchanged.
    /// On Windows it is re-imported through a PFX round-trip because SChannel refuses
    /// client-authentication certificates whose private key is ephemeral, which is exactly
    /// what <c>CreateFromPemFile</c> produces. The import deliberately uses the default key
    /// storage flags: the key is written to a temporary per-user container that Windows
    /// deletes when the certificate is disposed. <see cref="X509KeyStorageFlags.PersistKeySet"/>
    /// (which would leak one key container per connection) and
    /// <see cref="X509KeyStorageFlags.EphemeralKeySet"/> (which SChannel cannot use for client
    /// auth) are both intentionally avoided.
    /// </para>
    /// <para>
    /// Ownership: the caller adds the single returned certificate to the connection's
    /// owned-certificate list, so <c>DisposeAsync</c> disposes it and thereby releases the
    /// temporary key container. On Windows the original ephemeral certificate is disposed
    /// here (it is never handed back), so there is no leak and no double-dispose.
    /// </para>
    /// </summary>
    private static X509Certificate2 LoadClientCertificate(string certPath, string keyPath)
    {
      var cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
      if (!OperatingSystem.IsWindows())
        return cert;

      var pfx = cert.Export(X509ContentType.Pfx);
      try
      {
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.DefaultKeySet);
#else
        return new X509Certificate2(pfx, (string)null, X509KeyStorageFlags.DefaultKeySet);
#endif
      }
      finally
      {
        Array.Clear(pfx, 0, pfx.Length);
        // The re-imported certificate replaces this one as the tracked/owned instance.
        cert.Dispose();
      }
    }

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
