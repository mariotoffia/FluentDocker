using System;
using System.Security.Cryptography.X509Certificates;

namespace FluentDocker.Drivers.Models.Connection
{
  public static class ClientCertificateLoader
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
    public static X509Certificate2 Load(string certPath, string keyPath)
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
  }
}
