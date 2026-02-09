using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IAuthDriver.
  /// Uses POST /auth for registry authentication.
  /// </summary>
  public class DockerApiAuthDriver : DockerApiDriverBase, IAuthDriver
  {
    public DockerApiAuthDriver(IDockerApiConnection connection) : base(connection) { }

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

      var result = await PostAsync("/auth", body, cancellationToken);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            result.StatusCode == 401
                ? ErrorCodes.Auth.InvalidCredentials
                : ErrorCodes.Auth.LoginFailed,
            CreateErrorContext("POST /auth", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    public Task<CommandResponse<Unit>> LogoutAsync(
        DriverContext context, string server = null,
        CancellationToken cancellationToken = default)
    {
      // Docker Engine API does not have a logout endpoint.
      // Logout is a client-side operation (removing stored credentials).
      return Task.FromResult(CommandResponse<Unit>.Ok(Unit.Default));
    }
  }
}
