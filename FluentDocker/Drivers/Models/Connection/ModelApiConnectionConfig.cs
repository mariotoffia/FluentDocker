using System;

namespace FluentDocker.Drivers.Models.Connection
{
  /// <summary>
  /// Transport configuration for a <see cref="ModelApiConnection"/>.
  /// </summary>
  public sealed class ModelApiConnectionConfig
  {
    /// <summary>
    /// Directory containing <c>ca.pem</c>/<c>cert.pem</c>/<c>key.pem</c> for TLS, if any.
    /// <c>ca.pem</c> is optional; when present it is the exclusive trust root (pin), when
    /// absent the system trust store validates the server certificate.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>Whether to verify the server's TLS certificate.</summary>
    public bool VerifyTls { get; set; } = true;

    /// <summary>
    /// Whether a bearer API key may be sent over an insecure transport (plaintext HTTP, or
    /// TLS with <see cref="VerifyTls"/> disabled) to a non-loopback TCP host.
    /// Defaults to <c>false</c>; loopback and unix-socket transports are always allowed.
    /// </summary>
    public bool AllowApiKeyOverInsecureTransport { get; set; }

    /// <summary>The socket/connect timeout.</summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Total wall-clock budget for a NON-streaming request: the header phase and the body-read
    /// phase share this single deadline (a request is bounded by one <c>RequestTimeout</c>, not
    /// one per phase). Streaming responses are governed by the first-byte / idle timeouts
    /// instead. The underlying <see cref="System.Net.Http.HttpClient.Timeout"/> is left
    /// infinite; this budget is enforced via a linked token plus a body-read deadline. Defaults
    /// high to accommodate slow inference.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// When true, a TLS certificate whose hostname/SAN does not match the connection host
    /// is still accepted provided the chain validates against the configured CA. Default
    /// false (strict). Set true only for IP-based connections to a known host.
    /// </summary>
    public bool AllowTlsHostnameMismatch { get; set; }

    /// <summary>
    /// Max time to wait for streaming response headers or first body bytes. Defaults to
    /// 10 minutes so cold model load has a wider first-byte budget than mid-stream reads.
    /// The budget applies separately to the header wait and the first body byte (worst
    /// case twice the value before any progress is required). Set to <c>null</c> to wait
    /// indefinitely, honoring only cancellation.
    /// </summary>
    public TimeSpan? StreamFirstByteTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Max time to wait between successive streamed body chunks.
    /// Defaults to 120 seconds. Set to <c>null</c> to explicitly opt out (wait indefinitely,
    /// honoring only cancellation).</summary>
    public TimeSpan? StreamReadIdleTimeout { get; set; } = TimeSpan.FromSeconds(120);
  }
}
