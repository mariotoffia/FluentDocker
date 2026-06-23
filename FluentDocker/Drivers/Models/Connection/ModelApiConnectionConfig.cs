using System;

namespace FluentDocker.Drivers.Models.Connection
{
  /// <summary>
  /// Transport configuration for a <see cref="ModelApiConnection"/>.
  /// </summary>
  public sealed class ModelApiConnectionConfig
  {
    /// <summary>The host URI (<c>tcp://</c>, <c>http://</c>, <c>https://</c>, <c>unix://</c>, <c>npipe://</c>).</summary>
    public string Host { get; set; }

    /// <summary>Directory containing <c>ca.pem</c>/<c>cert.pem</c>/<c>key.pem</c> for TLS, if any.</summary>
    public string CertificatePath { get; set; }

    /// <summary>Whether to verify the server's TLS certificate.</summary>
    public bool VerifyTls { get; set; } = true;

    /// <summary>The socket/connect timeout.</summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The request timeout (HttpClient.Timeout). Defaults high to accommodate slow inference.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(10);
  }
}
