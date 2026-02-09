using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json.Linq;

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
          args += $" --driver {config.Driver}";
        if (!string.IsNullOrEmpty(config.Subnet))
          args += $" --subnet {config.Subnet}";
        if (!string.IsNullOrEmpty(config.Gateway))
          args += $" --gateway {config.Gateway}";
        if (config.EnableIPv6)
          args += " --ipv6";
        if (config.Internal)
          args += " --internal";

        foreach (var opt in config.Options)
          args += $" --opt {opt.Key}={opt.Value}";
        foreach (var label in config.Labels)
          args += $" --label {label.Key}={label.Value}";

        if (!string.IsNullOrEmpty(config.Name))
          args += $" {config.Name}";

        var result = await ExecuteCommandAsync(args, cancellationToken);
        if (!result.Success)
          return CommandResponse<NetworkCreateResult>.Fail(
              result.Error ?? "Network create failed", ErrorCodes.Network.CreateFailed);

        return CommandResponse<NetworkCreateResult>.Ok(new NetworkCreateResult
        {
          Id = result.Output?.Trim()
        });
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
        var result = await ExecuteCommandAsync($"network rm {networkId}", cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Network remove failed", ErrorCodes.Network.RemoveFailed);
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
            args += $" --filter name={filter.Name}";
          if (filter.Labels != null)
            foreach (var label in filter.Labels)
              args += $" --filter label={label.Key}={label.Value}";
        }

        var result = await ExecuteCommandAsync(args, cancellationToken);
        if (!result.Success)
          return CommandResponse<IList<Network>>.Fail(
              result.Error ?? "Network list failed", ErrorCodes.General.Unknown);

        var networks = ParseNetworkList(result.Output);
        return CommandResponse<IList<Network>>.Ok(networks);
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
            $"network connect {networkId} {containerId}", cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Network connect failed", ErrorCodes.Network.ConnectFailed);
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
            ? $"network disconnect -f {networkId} {containerId}"
            : $"network disconnect {networkId} {containerId}";

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Network disconnect failed", ErrorCodes.Network.DisconnectFailed);
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
            $"network inspect {networkId}", cancellationToken);
        if (!result.Success)
          return CommandResponse<Network>.Fail(
              result.Error ?? "Network inspect failed", ErrorCodes.Network.InspectFailed);

        var network = ParseNetworkInspect(result.Output);
        return CommandResponse<Network>.Ok(network);
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
        var result = await ExecuteCommandAsync("network prune -f", cancellationToken);
        if (!result.Success)
          return CommandResponse<NetworkPruneResult>.Fail(
              result.Error ?? "Network prune failed", ErrorCodes.Network.PruneFailed);

        return CommandResponse<NetworkPruneResult>.Ok(new NetworkPruneResult());
      }
      catch (Exception ex)
      {
        return CommandResponse<NetworkPruneResult>.Fail(ex.Message, ErrorCodes.Network.PruneFailed);
      }
    }

    #region JSON Parsing

    private static IList<Network> ParseNetworkList(string json)
    {
      var networks = new List<Network>();
      if (string.IsNullOrWhiteSpace(json))
        return networks;

      try
      {
        var trimmed = json.Trim();
        if (trimmed.StartsWith("["))
        {
          var arr = JArray.Parse(trimmed);
          foreach (var token in arr)
            networks.Add(ParseNetworkFromToken(token));
        }
        else
        {
          foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            networks.Add(ParseNetworkFromToken(JObject.Parse(line.Trim())));
        }
      }
      catch { /* Return partial results */ }

      return networks;
    }

    private static Network ParseNetworkFromToken(JToken token)
    {
      return new Network
      {
        Id = token["ID"]?.Value<string>() ?? token["Id"]?.Value<string>(),
        Name = token["Name"]?.Value<string>() ?? token["name"]?.Value<string>(),
        Driver = token["Driver"]?.Value<string>() ?? token["driver"]?.Value<string>(),
        Scope = token["Scope"]?.Value<string>(),
        IPv6 = token["IPv6Enabled"]?.Value<bool>() ?? token["ipv6_enabled"]?.Value<bool>() ?? false,
        Internal = token["Internal"]?.Value<bool>() ?? token["internal"]?.Value<bool>() ?? false,
        Labels = token["Labels"]?.ToObject<Dictionary<string, string>>()
                   ?? new Dictionary<string, string>()
      };
    }

    private static Network ParseNetworkInspect(string json)
    {
      try
      {
        var trimmed = json.Trim();
        JToken token;
        if (trimmed.StartsWith("["))
        {
          var arr = JArray.Parse(trimmed);
          token = arr.First;
        }
        else
        {
          token = JObject.Parse(trimmed);
        }

        return ParseNetworkFromToken(token);
      }
      catch
      {
        return new Network();
      }
    }

    #endregion
  }
}
