using FluentDocker.Model.Common;

namespace FluentDocker.Drivers.Docker.Cli.Binary
{
  /// <summary>
  /// Configuration for Docker binary resolution and execution.
  /// </summary>
  public class BinaryConfiguration
  {
    /// <summary>
    /// Gets or sets the sudo mechanism to use when executing Docker commands.
    /// </summary>
    public SudoMechanism Sudo { get; set; } = SudoMechanism.None;

    /// <summary>
    /// Gets or sets the password for sudo (when Sudo is set to Password).
    /// </summary>
    public string SudoPassword { get; set; }

    /// <summary>
    /// Gets or sets the default shell to use for sudo commands.
    /// Defaults to "bash".
    /// </summary>
    public string DefaultShell { get; set; } = "bash";

    /// <summary>
    /// Gets or sets custom search paths for Docker binaries.
    /// If null or empty, uses PATH environment variable.
    /// </summary>
    public string[] SearchPaths { get; set; }

    /// <summary>
    /// Creates a new instance with default settings.
    /// </summary>
    public BinaryConfiguration()
    {
    }

    /// <summary>
    /// Creates a new instance with the specified sudo mechanism.
    /// </summary>
    /// <param name="sudo">The sudo mechanism to use.</param>
    /// <param name="password">The sudo password (if required).</param>
    public BinaryConfiguration(SudoMechanism sudo, string password = null)
    {
      Sudo = sudo;
      SudoPassword = password;
    }
  }
}
