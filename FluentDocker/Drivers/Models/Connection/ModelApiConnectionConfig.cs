using System;

namespace FluentDocker.Drivers.Models.Connection
{
  /// <summary>
  /// Transport configuration for a <see cref="ModelApiConnection"/>.
  /// </summary>
  public sealed class ModelApiConnectionConfig
  {
    /// <summary>Directory containing <c>ca.pem</c>/<c>cert.pem</c>/<c>key.pem</c> for TLS, if any.</summary>
    public string CertificatePath { get; set; }

    /// <summary>Whether to verify the server's TLS certificate.</summary>
    /// <remarks>Setting this <c>false</c> also acknowledges an insecure transport: it permits
    /// sending the API key as a bearer token over plaintext HTTP to a non-loopback host.</remarks>
    public bool VerifyTls { get; set; } = true;

    /// <summary>The socket/connect timeout.</summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The request timeout (HttpClient.Timeout). Defaults high to accommodate slow inference.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// When true, a TLS certificate whose hostname/SAN does not match the connection host
    /// is still accepted provided the chain validates against the configured CA. Default
    /// false (strict). Set true only for IP-based connections to a known host.
    /// </summary>
    public bool AllowTlsHostnameMismatch { get; set; }

    /// <summary>Max time to wait for the next streamed chunk before aborting the read.
    /// Null disables the idle timeout (wait indefinitely, honoring only cancellation).</summary>
    public TimeSpan? StreamReadIdleTimeout { get; set; }
  }
}
