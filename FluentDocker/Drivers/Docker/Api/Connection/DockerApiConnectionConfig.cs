using System;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  /// <summary>
  /// Configuration for connecting to the Docker Engine REST API.
  /// </summary>
  public class DockerApiConnectionConfig
  {
    /// <summary>
    /// Docker daemon host URI. Supported schemes: unix://, npipe://, tcp://, http://, https://.
    /// Default: auto-detected based on platform.
    /// </summary>
    public string Host { get; set; }

    /// <summary>
    /// Path to directory containing TLS client certificates (ca.pem, cert.pem, key.pem).
    /// Setting this explicitly enables TLS and selects port 2376 for a bare tcp host.
    /// </summary>
    public string CertificatePath { get; set; }

    /// <summary>
    /// Whether to verify TLS certificates. Default: true.
    /// </summary>
    public bool VerifyTls { get; set; } = true;

    /// <summary>
    /// Connection timeout. Default: 30 seconds.
    /// </summary>
    /// <remarks>
    /// Must be strictly positive, or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to
    /// disable time bounds. Besides connect/TTFB, this value bounds the upload stall watchdog (the
    /// maximum time a body-bearing request may make no write progress before it is cancelled), so a
    /// zero or negative-but-finite value is rejected by the connection constructor. Setting it to
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> disables the upload stall watchdog
    /// (slow uploads are never cancelled for lack of progress).
    /// </remarks>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// HTTP request timeout. Default: 5 minutes (long for build/pull operations).
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Optional read-idle timeout for streamed response bodies. Null means disabled (default).
    /// </summary>
    /// <remarks>
    /// Applies to EVERY streamed response on this connection, not just logs/pull/push: this
    /// includes long-lived <c>/events</c>, stats, and attach streams. A legitimately idle
    /// stream (e.g. an events feed with no activity, or a container that is quiet for a while)
    /// is torn down every time this window elapses without a byte arriving — set it well above
    /// the longest expected legitimate idle gap, or leave it disabled for long-lived streams.
    /// When the timeout fires, the abandoned in-flight read briefly retains its rented
    /// <see cref="System.Buffers.ArrayPool{T}"/> buffer (it may still be writing into it); the
    /// buffer is returned to the pool only once that read settles, so a fully wedged connection
    /// can pin one buffer until its socket dies. Enable only once these trade-offs are acceptable
    /// for your streams.
    /// </remarks>
    public TimeSpan? StreamIdleTimeout { get; set; }

    /// <summary>
    /// Docker Engine API version to use. Null means auto-negotiate via /_ping.
    /// </summary>
    public string ApiVersion { get; set; }

    /// <summary>
    /// When true, a TLS certificate whose hostname/SAN does not match the connection host
    /// is still accepted provided the chain validates against the configured CA. Default
    /// false (strict). Set true only for IP-based connections to a known host.
    /// </summary>
    public bool AllowTlsHostnameMismatch { get; set; }

    internal bool UseTls { get; set; }
  }
}
