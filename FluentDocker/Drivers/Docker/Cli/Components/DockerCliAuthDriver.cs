using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI implementation of IAuthDriver.
  /// </summary>
  public class DockerCliAuthDriver : DockerCliDriverBase, IAuthDriver
  {
    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    public DockerCliAuthDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <summary>
    /// Builds CLI arguments and optional stdin data for <c>docker login</c>.
    /// </summary>
    public static (string args, string stdinData) BuildLoginArgs(RegistryLoginConfig config)
    {
      var args = "login";
      if (!string.IsNullOrEmpty(config.Username))
        args += $" -u {config.Username}";
      if (config.PasswordStdin)
      {
        args += " --password-stdin";
      }
      else if (!string.IsNullOrEmpty(config.Password))
      {
        args += $" -p {config.Password}";
      }
      if (!string.IsNullOrEmpty(config.Server))
        args += $" {config.Server}";

      var stdinData = config.PasswordStdin && !string.IsNullOrEmpty(config.Password)
          ? config.Password
          : null;

      return (args, stdinData);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> LoginAsync(
        DriverContext context,
        RegistryLoginConfig config,
        CancellationToken cancellationToken = default)
    {
      if (config.PasswordStdin && string.IsNullOrEmpty(config.Password))
        return CommandResponse<Unit>.Fail(
            "PasswordStdin is true but no password was provided",
            ErrorCodes.Auth.LoginFailed);

      try
      {
        var (args, stdinData) = BuildLoginArgs(config);
        var result = stdinData != null
            ? await ExecuteCommandAsync(args, stdinData, cancellationToken)
            : await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Login failed", ErrorCodes.Auth.LoginFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Auth.LoginFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> LogoutAsync(
        DriverContext context,
        string server = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "logout";
        if (!string.IsNullOrEmpty(server))
          args += $" {server}";

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Logout failed", ErrorCodes.Auth.LogoutFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Auth.LogoutFailed);
      }
    }
  }
}

