using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI implementation of INetworkDriver.
  /// </summary>
  public class DockerCliNetworkDriver : DockerCliDriverBase, INetworkDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];
    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    public DockerCliNetworkDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <inheritdoc />
    public async Task<CommandResponse<NetworkCreateResult>> CreateAsync(
        DriverContext context,
        NetworkCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = new List<string> { "network", "create" };

        if (!string.IsNullOrEmpty(config.Driver))
          args.Add($"--driver {QuoteArgumentIfNeeded(config.Driver)}");

        if (!string.IsNullOrEmpty(config.Subnet))
          args.Add($"--subnet {QuoteArgumentIfNeeded(config.Subnet)}");

        if (!string.IsNullOrEmpty(config.Gateway))
          args.Add($"--gateway {QuoteArgumentIfNeeded(config.Gateway)}");

        if (!string.IsNullOrEmpty(config.IpRange))
          args.Add($"--ip-range {QuoteArgumentIfNeeded(config.IpRange)}");

        if (config.EnableIPv6)
          args.Add("--ipv6");

        if (config.Internal)
          args.Add("--internal");

        if (config.Options != null)
        {
          foreach (var opt in config.Options)
            args.Add($"--opt {QuoteArgumentIfNeeded($"{opt.Key}={opt.Value}")}");
        }

        if (config.Labels != null)
        {
          foreach (var label in config.Labels)
            args.Add($"--label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}");
        }

        if (!string.IsNullOrEmpty(config.Name))
          args.Add(QuotePositionalArgument(config.Name, nameof(config.Name)));

        var result = await ExecuteCommandAsync(context, string.Join(" ", args), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<NetworkCreateResult>.Fail(
              ErrorOrDefault(result, "Network creation failed"),
              FailureCode(result.Error, ErrorCodes.Network.CreateFailed),
              CreateErrorContext(context, "CreateNetwork", result),
              result.ExitCode);
        }

        return CommandResponse<NetworkCreateResult>.Ok(
            new NetworkCreateResult { Id = result.Output.Trim() });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<NetworkCreateResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Network.CreateFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string networkId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"network rm {QuotePositionalArgument(networkId, nameof(networkId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Network removal failed"),
              FailureCode(result.Error, ErrorCodes.Network.RemoveFailed),
              CreateErrorContext(context, "RemoveNetwork", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Network.RemoveFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Network>>> ListAsync(
        DriverContext context,
        NetworkListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "network ls --format \"{{json .}}\"";

        if (filter != null)
        {
          if (!string.IsNullOrEmpty(filter.Name))
            args += $" --filter {QuoteArgumentIfNeeded($"name={filter.Name}")}";

          if (filter.Labels != null)
          {
            foreach (var label in filter.Labels)
              args += $" --filter {QuoteArgumentIfNeeded($"label={label.Key}={label.Value}")}";
          }
        }

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<Network>>.Fail(
              ErrorOrDefault(result, "Network list failed"),
              FailureCode(result.Error, ErrorCodes.General.Unknown),
              CreateErrorContext(context, "ListNetworks", result),
              result.ExitCode);
        }

        if (!DockerCliJsonLineParser.TryParse(
                result.Output,
                Logger,
                "Network list JSON parsing failed",
                out List<Network> networks,
                out var parseError))
        {
          return CommandResponse<IList<Network>>.Fail(parseError, ErrorCodes.General.Unknown);
        }

        return CommandResponse<IList<Network>>.Ok(networks);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Network>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Network>> InspectAsync(
        DriverContext context,
        string networkId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"network inspect {QuotePositionalArgument(networkId, nameof(networkId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Network>.Fail(
              ErrorOrDefault(result, "Network inspect failed"),
              result.Error?.Contains("No such network", StringComparison.OrdinalIgnoreCase) == true
                  ? ErrorCodes.Network.NotFound
                  : FailureCode(result.Error, ErrorCodes.Network.InspectFailed),
              CreateErrorContext(context, "InspectNetwork", result),
              result.ExitCode);
        }

        var networks = JsonHelper.TryDeserialize<List<Network>>(result.Output);
        if (networks == null)
        {
          Logger.LogError("Network inspect JSON parsing failed");
          return CommandResponse<Network>.Fail("Network inspect JSON parsing failed", ErrorCodes.Network.InspectFailed);
        }
        var network = networks?.FirstOrDefault();
        return network == null
            ? CommandResponse<Network>.Fail($"Network '{networkId}' was not found", ErrorCodes.Network.NotFound)
            : CommandResponse<Network>.Ok(network);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Network>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Network.InspectFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ConnectAsync(
        DriverContext context,
        string networkId,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"network connect {QuotePositionalArgument(networkId, nameof(networkId))} {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Network connect failed"),
              FailureCode(result.Error, ErrorCodes.Network.ConnectFailed),
              CreateErrorContext(context, "ConnectNetwork", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Network.ConnectFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> DisconnectAsync(
        DriverContext context,
        string networkId,
        string containerId,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "network disconnect";
        if (force)
          args += " --force";
        args += $" {QuotePositionalArgument(networkId, nameof(networkId))} {QuotePositionalArgument(containerId, nameof(containerId))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Network disconnect failed"),
              FailureCode(result.Error, ErrorCodes.Network.DisconnectFailed),
              CreateErrorContext(context, "DisconnectNetwork", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Network.DisconnectFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<NetworkPruneResult>> PruneAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, "network prune --force", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<NetworkPruneResult>.Fail(
              ErrorOrDefault(result, "Network prune failed"),
              FailureCode(result.Error, ErrorCodes.Network.PruneFailed));
        }

        return CommandResponse<NetworkPruneResult>.Ok(
            CliPruneOutputParser.ParseNetworkPruneOutput(result.Output));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<NetworkPruneResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Network.PruneFailed));
      }
    }
  }
}
