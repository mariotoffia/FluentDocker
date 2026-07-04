using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of IAuthDriver.
  /// </summary>
  public class PodmanCliAuthDriver(IPodmanBinaryResolver binaryResolver) : PodmanCliDriverBase(binaryResolver), IAuthDriver
  {

    /// <summary>
    /// Builds CLI arguments and optional stdin data for <c>podman login</c>.
    /// The password is always passed via stdin (--password-stdin) and never
    /// placed on the command line, to prevent exposure in process listings.
    /// </summary>
    public static (string args, string stdinData) BuildLoginArgs(RegistryLoginConfig config)
    {
      var args = "login";
      if (!string.IsNullOrEmpty(config.Username))
        args += $" -u {QuoteArgumentIfNeeded(config.Username)}";

      // Always use --password-stdin when a password is provided.
      // Never pass password via -p flag (visible in process listings).
      if (!string.IsNullOrEmpty(config.Password))
        args += " --password-stdin";

      if (!string.IsNullOrEmpty(config.Server))
        args += $" {QuotePositionalArgument(config.Server, nameof(config.Server))}";

      var stdinData = !string.IsNullOrEmpty(config.Password) ? config.Password : null;

      return (args, stdinData);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> LoginAsync(
        DriverContext context, RegistryLoginConfig config,
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
            ? await ExecuteCommandAsync(context, args, stdinData, cancellationToken).ConfigureAwait(false)
            : await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Login failed"), FailureCode(result.Error, ErrorCodes.Auth.LoginFailed),
              CreateErrorContext(context, "Login", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Auth.LoginFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> LogoutAsync(
        DriverContext context, string server = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "logout";
        if (!string.IsNullOrEmpty(server))
          args += $" {QuotePositionalArgument(server, nameof(server))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Logout failed"), FailureCode(result.Error, ErrorCodes.Auth.LogoutFailed),
              CreateErrorContext(context, "Logout", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Auth.LogoutFailed));
      }
    }
  }
}
