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

    /// <summary>
    /// Drives a docker-compatible CLI other than <c>docker</c> (best-effort) — for example
    /// <c>finch</c> or <c>nerdctl</c> — without aliasing it to <c>docker</c>. Note that some
    /// engines differ on a few global flags (e.g. <c>-H</c>/<c>--tlsverify</c>).
    /// </summary>
    /// <param name="binaryName">The client binary name, e.g. "finch" or "nerdctl".</param>
    /// <param name="searchPaths">
    /// Optional directories to search for the binary. When omitted, <c>PATH</c> is used.
    /// </param>
    IDockerCliDriverBuilder WithBinary(string binaryName, params string[] searchPaths);
  }
}
