using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.ApiModels;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IAuthDriver.
  /// Uses POST /auth for registry authentication.
  /// </summary>
  public class DockerApiAuthDriver(IDockerApiConnection connection) : DockerApiDriverBase(connection), IAuthDriver
  {
    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> LoginAsync(
        DriverContext context, RegistryLoginConfig config,
        CancellationToken cancellationToken = default)
    {
      var body = new
      {
        username = config.Username,
        password = config.Password,
        serveraddress = config.Server ?? "https://index.docker.io/v1/"
      };

      var result = await PostJsonAsync<DockerApiAuthResponse>("/auth", body, cancellationToken)
          .ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            result.StatusCode == 401
                ? ErrorCodes.Auth.InvalidCredentials
                : ErrorCodes.Auth.LoginFailed,
            CreateErrorContext("POST /auth", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      // Prefer the registry's identity token (Docker Hub PAT/2FA, cloud registries) over the raw
      // credentials on subsequent X-Registry-Auth headers (API-MAJ-2).
      if (!string.IsNullOrEmpty(result.Data?.IdentityToken))
        config.IdentityToken = result.Data.IdentityToken;

      DockerApiRegistryAuth.Store(Connection, config);
      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    /// <remarks>
    /// Docker Engine API has no logout endpoint. Logout is a client-side
    /// operation (removing stored credentials from the Docker config file).
    /// Returns <c>Ok</c> since there is nothing to undo server-side.
    /// </remarks>
    public Task<CommandResponse<Unit>> LogoutAsync(
        DriverContext context, string? server = null,
        CancellationToken cancellationToken = default)
    {
      DockerApiRegistryAuth.Remove(Connection, server);
      return Task.FromResult(CommandResponse<Unit>.Ok(Unit.Default));
    }
  }
}
