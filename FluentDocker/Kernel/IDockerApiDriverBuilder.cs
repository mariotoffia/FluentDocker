using System;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Type-safe builder for configuring the Docker API driver.
  /// Exposes only settings applicable to direct Docker Engine REST API communication.
  /// </summary>
  public interface IDockerApiDriverBuilder
  {
    /// <summary>
    /// Sets the Docker daemon host URI.
    /// </summary>
    /// <param name="host">Host URI (e.g., "unix:///var/run/docker.sock", "tcp://localhost:2376")</param>
    IDockerApiDriverBuilder AtHost(string host);

    /// <summary>
    /// Sets the certificate path for TLS connections.
    /// </summary>
    /// <param name="certificatePath">Path to certificate directory</param>
    IDockerApiDriverBuilder WithCertificates(string certificatePath);

    /// <summary>
    /// Sets this driver as the default.
    /// </summary>
    IDockerApiDriverBuilder AsDefault();

    /// <summary>
    /// Sets the HTTP connection timeout.
    /// </summary>
    /// <param name="timeout">Connection timeout (default: 30 seconds)</param>
    IDockerApiDriverBuilder WithConnectionTimeout(TimeSpan timeout);

    /// <summary>
    /// Sets the HTTP request timeout for long-running operations (build, pull).
    /// </summary>
    /// <param name="timeout">Request timeout (default: 5 minutes)</param>
    IDockerApiDriverBuilder WithRequestTimeout(TimeSpan timeout);

    /// <summary>
    /// Sets a specific Docker Engine API version instead of auto-negotiating.
    /// </summary>
    /// <param name="version">API version (e.g., "1.41"). Null for auto-negotiation.</param>
    IDockerApiDriverBuilder WithApiVersion(string version);

    /// <summary>
    /// Enables or disables TLS certificate verification.
    /// </summary>
    /// <param name="verify">Whether to verify TLS certificates (default: true)</param>
    IDockerApiDriverBuilder WithTlsVerification(bool verify = true);
  }
}
