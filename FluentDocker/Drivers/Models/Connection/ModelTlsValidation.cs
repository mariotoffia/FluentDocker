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
    /// Validates a server certificate against a custom root CA <b>without</b> relaxing
    /// hostname verification. A hostname mismatch
    /// (<see cref="SslPolicyErrors.RemoteCertificateNameMismatch"/>) or a missing
    /// certificate (<see cref="SslPolicyErrors.RemoteCertificateNotAvailable"/>) is
    /// always rejected — even with a configured CA — so a certificate signed by the
    /// CA for a different host cannot MITM the connection. Only a chain-trust error
    /// (<see cref="SslPolicyErrors.RemoteCertificateChainErrors"/>) is re-validated
    /// against <paramref name="caCert"/>.
    /// </summary>
    /// <param name="caCert">The custom root CA to trust for chain validation.</param>
    /// <param name="cert">The presented server certificate.</param>
    /// <param name="chain">The certificate chain.</param>
    /// <param name="errors">The default policy errors reported by the platform.</param>
    /// <returns><c>true</c> only when the certificate is acceptable.</returns>
    public static bool ValidateWithCustomRoot(X509Certificate2 caCert, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
    {
      if (errors == SslPolicyErrors.None)
        return true;

      // Never accept a hostname mismatch or a missing certificate, even with a custom
      // root — those are not chain-trust problems and a custom CA must not paper over them.
      if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
        return false;
      if ((errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
        return false;

      // Only the chain-trust error is eligible for custom-root re-validation.
      if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
        return false;
      if (caCert == null || chain == null || cert == null)
        return false;

      chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
      chain.ChainPolicy.CustomTrustStore.Add(caCert);

      if (cert is X509Certificate2 server)
        return chain.Build(server);

      using var copy = new X509Certificate2(cert);
      return chain.Build(copy);
    }
  }
}
