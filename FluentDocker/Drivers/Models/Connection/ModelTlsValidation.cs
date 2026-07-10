using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace FluentDocker.Drivers.Models.Connection
{
  /// <summary>
  /// TLS certificate validation helpers for <see cref="ModelApiConnection"/>.
  /// </summary>
  public static class ModelTlsValidation
  {
    /// <summary>
    /// Validates a server certificate against a custom root CA. By default, a hostname
    /// mismatch (<see cref="SslPolicyErrors.RemoteCertificateNameMismatch"/>) or a missing
    /// certificate (<see cref="SslPolicyErrors.RemoteCertificateNotAvailable"/>) is
    /// always rejected — even with a configured CA — so a certificate signed by the
    /// CA for a different host cannot MITM the connection. Only a chain-trust error
    /// (<see cref="SslPolicyErrors.RemoteCertificateChainErrors"/>) is re-validated
    /// against <paramref name="caCert"/>. When <paramref name="allowHostnameMismatch"/> is
    /// <c>true</c>, a name-mismatch error is suppressed (the chain is still validated).
    /// </summary>
    /// <param name="caCert">The custom root CA to trust for chain validation.</param>
    /// <param name="cert">The presented server certificate.</param>
    /// <param name="chain">The certificate chain.</param>
    /// <param name="errors">The default policy errors reported by the platform.</param>
    /// <param name="allowHostnameMismatch">
    /// When <c>true</c>, a TLS certificate whose hostname/SAN does not match the connection
    /// host is still accepted provided the chain validates against the configured CA. Default
    /// <c>false</c> (strict). Set <c>true</c> only for IP-based connections to a known host.
    /// </param>
    /// <returns><c>true</c> only when the certificate is acceptable.</returns>
    public static bool ValidateWithCustomRoot(X509Certificate2 caCert, X509Certificate cert, X509Chain chain,
        SslPolicyErrors errors, bool allowHostnameMismatch = false) =>
        ValidateWithCustomRoot(
            caCert == null ? null : new X509Certificate2Collection(caCert),
            cert, chain, errors, allowHostnameMismatch);

    /// <summary>
    /// Custom-root validation against a full CA bundle (intermediate + root). Enterprise
    /// <c>ca.pem</c> files routinely ship several PEM blocks and the Go docker CLI trusts all of
    /// them, so loading only the first cert rejected valid chains (DAPI-MAJ-3). All certs in
    /// <paramref name="caCerts"/> are added to the custom trust store.
    /// </summary>
    public static bool ValidateWithCustomRoot(X509Certificate2Collection caCerts, X509Certificate cert, X509Chain chain,
        SslPolicyErrors errors, bool allowHostnameMismatch = false)
    {
      // A missing certificate is never acceptable.
      if ((errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
        return false;

      var remaining = errors;
      if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
      {
        if (!allowHostnameMismatch)
          return false;                 // default: reject hostname/SAN mismatch
        remaining &= ~SslPolicyErrors.RemoteCertificateNameMismatch; // opted in: ignore it
      }

      // Only the chain-trust error is eligible for custom-root re-validation.
      if ((remaining & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
        return false;
      if (caCerts == null || caCerts.Count == 0 || chain == null || cert == null)
        return false;

      chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
      chain.ChainPolicy.CustomTrustStore.AddRange(caCerts);

      if (cert is X509Certificate2 server)
        return chain.Build(server);

      using var copy = new X509Certificate2(cert);
      return chain.Build(copy);
    }
  }
}
