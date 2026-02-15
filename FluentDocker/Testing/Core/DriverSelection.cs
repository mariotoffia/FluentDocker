using FluentDocker.Model.Drivers;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Specifies which driver a test resource should use.
  /// </summary>
  public class DriverSelection
  {
    /// <summary>
    /// Use the kernel's default driver.
    /// </summary>
    public static readonly DriverSelection Default = new() { UseDefault = true };

    /// <summary>
    /// Whether to use the kernel's default driver.
    /// </summary>
    public bool UseDefault { get; init; }

    /// <summary>
    /// Explicit driver ID to use (when <see cref="UseDefault"/> is false).
    /// </summary>
    public string DriverId { get; init; }

    /// <summary>
    /// Expected driver type for preflight validation.
    /// Null means any type is acceptable.
    /// </summary>
    public DriverType? ExpectedType { get; init; }

    /// <summary>
    /// Creates a selection for a specific driver ID.
    /// </summary>
    public static DriverSelection Specific(string driverId, DriverType? expectedType = null)
    {
      return new DriverSelection
      {
        UseDefault = false,
        DriverId = driverId,
        ExpectedType = expectedType
      };
    }

    /// <summary>
    /// Creates a selection for Docker CLI.
    /// </summary>
    public static DriverSelection DockerCli(string driverId = "docker-cli")
    {
      return Specific(driverId, DriverType.DockerCli);
    }

    /// <summary>
    /// Creates a selection for Docker API.
    /// </summary>
    public static DriverSelection DockerApi(string driverId = "docker-api")
    {
      return Specific(driverId, DriverType.DockerApi);
    }

    /// <summary>
    /// Creates a selection for Podman CLI.
    /// </summary>
    public static DriverSelection PodmanCli(string driverId = "podman-cli")
    {
      return Specific(driverId, DriverType.PodmanCli);
    }
  }
}
