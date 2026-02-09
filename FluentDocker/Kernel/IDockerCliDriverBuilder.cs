using FluentDocker.Model.Common;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Type-safe builder for configuring the Docker CLI driver.
  /// Exposes only settings applicable to the Docker CLI driver.
  /// </summary>
  public interface IDockerCliDriverBuilder
  {
    /// <summary>
    /// Sets the Docker daemon host URI.
    /// </summary>
    /// <param name="host">Host URI (e.g., "unix:///var/run/docker.sock", "tcp://localhost:2376")</param>
    IDockerCliDriverBuilder AtHost(string host);

    /// <summary>
    /// Sets the certificate path for TLS connections.
    /// </summary>
    /// <param name="certificatePath">Path to certificate directory</param>
    IDockerCliDriverBuilder WithCertificates(string certificatePath);

    /// <summary>
    /// Sets this driver as the default.
    /// </summary>
    IDockerCliDriverBuilder AsDefault();

    /// <summary>
    /// Configures sudo mechanism for Docker CLI commands (Linux).
    /// </summary>
    /// <param name="mechanism">Sudo mechanism to use</param>
    /// <param name="password">Password when using <see cref="SudoMechanism.Password"/></param>
    IDockerCliDriverBuilder WithSudo(SudoMechanism mechanism, string password = null);
  }
}
