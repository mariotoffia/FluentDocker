using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
    /// <summary>
    /// Docker CLI implementation of IAuthDriver.
    /// </summary>
    public class DockerCliAuthDriver : DockerCliDriverBase, IAuthDriver
    {
        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> LoginAsync(
            DriverContext context,
            RegistryLoginConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "login";
                if (!string.IsNullOrEmpty(config.Username))
                    args += $" -u {config.Username}";
                if (!string.IsNullOrEmpty(config.Password) && !config.PasswordStdin)
                    args += $" -p {config.Password}";
                if (config.PasswordStdin)
                    args += " --password-stdin";
                if (!string.IsNullOrEmpty(config.Server))
                    args += $" {config.Server}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
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

