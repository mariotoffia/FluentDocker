using System;
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
    /// On Windows it is re-imported through a PFX round-trip: SChannel refuses
    /// client-authentication certificates whose private key is EPHEMERAL — which is exactly
    /// what <c>CreateFromPemFile</c> produces — so mTLS would otherwise fail silently during
    /// the TLS handshake. Re-importing with <see cref="X509KeyStorageFlags.PersistKeySet"/>
    /// yields a PERSISTED (non-ephemeral) key SChannel accepts; <see cref="X509KeyStorageFlags.Exportable"/>
    /// preserves the original's ability to export the key.
    /// </para>
    /// <para>
    /// Ownership is preserved exactly as before: the caller adds the single returned
    /// certificate to the connection's owned-certificate list for disposal. On Windows the
    /// original ephemeral certificate is disposed here (it is never handed back), so there is
    /// no leak and no double-dispose.
    /// </para>
    /// </summary>
    private static X509Certificate2 LoadClientCertificate(string certPath, string keyPath)
    {
      var cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
      if (!OperatingSystem.IsWindows())
        return cert;

      // Windows-only: persist the private key by round-tripping through PFX. The PFX bytes
      // carry the key in the clear but live only on the stack for the duration of the import.
      var pfx = cert.Export(X509ContentType.Pfx);
      try
      {
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(
            pfx, null, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
#else
        return new X509Certificate2(
            pfx, (string)null, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
#endif
      }
      finally
      {
        // The re-imported certificate replaces this one as the tracked/owned instance.
        cert.Dispose();
      }
    }
  }
}
