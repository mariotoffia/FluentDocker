using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Authentication driver for registry login/logout operations.
  /// Supported by: Docker, Podman.
  /// </summary>
  public interface IAuthDriver
  {
    /// <summary>
    /// Logs in to a container registry.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="config">Login configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<CommandResponse<Unit>> LoginAsync(
        DriverContext context,
        RegistryLoginConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out from a container registry.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="server">Registry server (null for Docker Hub)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<CommandResponse<Unit>> LogoutAsync(
        DriverContext context,
        string? server = null,
        CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Configuration for registry login.
  /// </summary>
  public class RegistryLoginConfig
  {
    /// <summary>
    /// Registry server URL (null for Docker Hub).
    /// </summary>
    public string? Server { get; set; }

    /// <summary>
    /// Username for authentication.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for authentication.
    /// </summary>
    [JsonIgnore]
    public string? Password { get; set; }

    /// <summary>
    /// When <c>true</c>, requires that <see cref="Password"/> is present — adapters fail fast if it
    /// is missing. This flag does not change how the password is transmitted: the password is
    /// always passed to the CLI via stdin when set; the flag only validates that one was supplied.
    /// </summary>
    public bool PasswordStdin { get; set; }

    /// <summary>
    /// Email address (deprecated in newer Docker versions).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Identity token returned by the registry's token/2FA flow. When set it is preferred over
    /// username/password in subsequent <c>X-Registry-Auth</c> headers (Docker Hub PAT/2FA and some
    /// cloud registries require this). Populated by the driver from the <c>POST /auth</c> response;
    /// callers do not normally set it.
    /// </summary>
    [JsonIgnore]
    public string? IdentityToken { get; set; }

    /// <summary>Returns a redacted representation of the registry login configuration.</summary>
    public override string ToString()
    {
      var password = string.IsNullOrEmpty(Password) ? "<null>" : "***";
      return $"RegistryLoginConfig(Server={Server ?? "<null>"}, Username={Username ?? "<null>"}, Password={password}, PasswordStdin={PasswordStdin}, Email={Email ?? "<null>"})";
    }
  }
}
