using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of INetworkDriver.
  /// Uses CNI/netavark plugins instead of Docker's bridge driver.
  /// </summary>
  public class PodmanCliNetworkDriver : PodmanCliDriverBase, INetworkDriver
  {
    public PodmanCliNetworkDriver(IPodmanBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <inheritdoc />
    public async Task<CommandResponse<NetworkCreateResult>> CreateAsync(
        DriverContext context, NetworkCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "network create";
        if (!string.IsNullOrEmpty(config.Driver))
          args += $" --driver {QuoteArgumentIfNeeded(config.Driver)}";
        if (!string.IsNullOrEmpty(config.Subnet))
          args += $" --subnet {QuoteArgumentIfNeeded(config.Subnet)}";
        if (!string.IsNullOrEmpty(config.Gateway))
          args += $" --gateway {QuoteArgumentIfNeeded(config.Gateway)}";
        if (config.EnableIPv6)
          args += " --ipv6";
        if (config.Internal)
          args += " --internal";

        foreach (var opt in config.Options)
          args += $" --opt {QuoteArgumentIfNeeded($"{opt.Key}={opt.Value}")}";
        foreach (var label in config.Labels)
          args += $" --label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}";

        if (!string.IsNullOrEmpty(config.Name))
          args += $" {QuoteArgumentIfNeeded(config.Name)}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<NetworkCreateResult>.Fail(
              result.Error ?? "Network create failed", ErrorCodes.Network.CreateFailed,
              CreateErrorContext(context, "CreateNetwork", result), result.ExitCode);

        return CommandResponse<NetworkCreateResult>.Ok(new NetworkCreateResult
        {
          Id = result.Output?.Trim()
        });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<NetworkCreateResult>.Fail(ex.Message, ErrorCodes.Network.CreateFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string networkId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"network rm {QuoteArgumentIfNeeded(networkId)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Network remove failed", ErrorCodes.Network.RemoveFailed,
              CreateErrorContext(context, "RemoveNetwork", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Network.RemoveFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Network>>> ListAsync(
        DriverContext context, NetworkListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "network ls --format json";
        if (filter != null)
        {
          if (!string.IsNullOrEmpty(filter.Name))
            args += $" --filter {QuoteArgumentIfNeeded($"name={filter.Name}")}";
          if (filter.Labels != null)
            foreach (var label in filter.Labels)
              args += $" --filter {QuoteArgumentIfNeeded($"label={label.Key}={label.Value}")}";
        }

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<Network>>.Fail(
              result.Error ?? "Network list failed", ErrorCodes.General.Unknown,
              CreateErrorContext(context, "ListNetworks", result), result.ExitCode);

        var networks = ParseNetworkList(result.Output);
        return CommandResponse<IList<Network>>.Ok(networks);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Network>>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ConnectAsync(
        DriverContext context, string networkId, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"network connect {QuoteArgumentIfNeeded(networkId)} {QuoteArgumentIfNeeded(containerId)}",
            cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Network connect failed", ErrorCodes.Network.ConnectFailed,
              CreateErrorContext(context, "ConnectNetwork", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Network.ConnectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> DisconnectAsync(
        DriverContext context, string networkId, string containerId, bool force = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = force
            ? $"network disconnect -f {QuoteArgumentIfNeeded(networkId)} {QuoteArgumentIfNeeded(containerId)}"
            : $"network disconnect {QuoteArgumentIfNeeded(networkId)} {QuoteArgumentIfNeeded(containerId)}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Network disconnect failed", ErrorCodes.Network.DisconnectFailed,
              CreateErrorContext(context, "DisconnectNetwork", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Network.DisconnectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Network>> InspectAsync(
        DriverContext context, string networkId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"network inspect {QuoteArgumentIfNeeded(networkId)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Network>.Fail(
              result.Error ?? "Network inspect failed", ErrorCodes.Network.InspectFailed,
              CreateErrorContext(context, "InspectNetwork", result), result.ExitCode);

        var network = ParseNetworkInspect(result.Output);
        return CommandResponse<Network>.Ok(network);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Network>.Fail(ex.Message, ErrorCodes.Network.InspectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<NetworkPruneResult>> PruneAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync("network prune -f", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<NetworkPruneResult>.Fail(
              result.Error ?? "Network prune failed", ErrorCodes.Network.PruneFailed,
              CreateErrorContext(context, "PruneNetworks", result), result.ExitCode);

        return CommandResponse<NetworkPruneResult>.Ok(
            CliPruneOutputParser.ParseNetworkPruneOutput(result.Output));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<NetworkPruneResult>.Fail(ex.Message, ErrorCodes.Network.PruneFailed);
      }
    }

    #region JSON Parsing

    private static List<Network> ParseNetworkList(string json)
    {
      var networks = new List<Network>();
      if (string.IsNullOrWhiteSpace(json))
        return networks;

      try
      {
        var trimmed = json.Trim();
        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          foreach (var token in root.EnumerateArraySafe())
            networks.Add(ParseNetworkFromToken(token));
        }
        else
        {
          foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            networks.Add(ParseNetworkFromToken(JsonHelper.ParseElement(line.Trim())));
        }
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman network list output: {ex.Message}");
      }

      return networks;
    }

    private static Network ParseNetworkFromToken(JsonElement token)
    {
      return new Network
      {
        Id = token.GetStringOrDefault("ID") ?? token.GetStringOrDefault("Id"),
        Name = token.GetStringOrDefault("Name") ?? token.GetStringOrDefault("name"),
        Driver = token.GetStringOrDefault("Driver") ?? token.GetStringOrDefault("driver"),
        Scope = token.GetStringOrDefault("Scope"),
        IPv6 = token.GetBoolOrDefault("IPv6Enabled") || token.GetBoolOrDefault("ipv6_enabled"),
        Internal = token.GetBoolOrDefault("Internal") || token.GetBoolOrDefault("internal"),
        Labels = token.GetStringDictionary("Labels")
      };
    }

    private static Network ParseNetworkInspect(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
        return new Network();

      try
      {
        var trimmed = json.Trim();
        JsonElement token;
        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          using var enumerator = root.EnumerateArray();
          if (!enumerator.MoveNext())
            return new Network();
          token = enumerator.Current;
        }
        else
        {
          token = JsonHelper.ParseElement(trimmed);
        }

        return ParseNetworkFromToken(token);
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman network inspect output: {ex.Message}");
      }
    }

    #endregion
  }
}
