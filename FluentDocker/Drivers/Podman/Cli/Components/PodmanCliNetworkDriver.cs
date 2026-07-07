using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Networks;

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
        if (!string.IsNullOrEmpty(config.IpRange))
          args += $" --ip-range {QuoteArgumentIfNeeded(config.IpRange)}";
        if (config.EnableIPv6)
          args += " --ipv6";
        if (config.Internal)
          args += " --internal";

        foreach (var opt in OrEmpty(config.Options))
          args += $" --opt {QuoteArgumentIfNeeded($"{opt.Key}={opt.Value}")}";
        foreach (var label in OrEmpty(config.Labels))
          args += $" --label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}";

        if (!string.IsNullOrEmpty(config.Name))
          args += $" {QuotePositionalArgument(config.Name, nameof(config.Name))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<NetworkCreateResult>.Fail(
              ErrorOrDefault(result, "Network create failed"), FailureCode(result.Error, ErrorCodes.Network.CreateFailed),
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
        return CommandResponse<NetworkCreateResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Network.CreateFailed));
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
            context,
            $"network rm {QuotePositionalArgument(networkId, nameof(networkId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Network remove failed"), FailureCode(result.Error, ErrorCodes.Network.RemoveFailed),
              CreateErrorContext(context, "RemoveNetwork", result), result.ExitCode);

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

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<Network>>.Fail(
              ErrorOrDefault(result, "Network list failed"), FailureCode(result.Error, ErrorCodes.General.Unknown),
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
        return CommandResponse<IList<Network>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
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
            context,
            $"network connect {QuotePositionalArgument(networkId, nameof(networkId))} {QuotePositionalArgument(containerId, nameof(containerId))}",
            cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Network connect failed"), FailureCode(result.Error, ErrorCodes.Network.ConnectFailed),
              CreateErrorContext(context, "ConnectNetwork", result), result.ExitCode);

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
        DriverContext context, string networkId, string containerId, bool force = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = force
            ? $"network disconnect -f {QuotePositionalArgument(networkId, nameof(networkId))} {QuotePositionalArgument(containerId, nameof(containerId))}"
            : $"network disconnect {QuotePositionalArgument(networkId, nameof(networkId))} {QuotePositionalArgument(containerId, nameof(containerId))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Network disconnect failed"), FailureCode(result.Error, ErrorCodes.Network.DisconnectFailed),
              CreateErrorContext(context, "DisconnectNetwork", result), result.ExitCode);

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
    public async Task<CommandResponse<Network>> InspectAsync(
        DriverContext context, string networkId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            context,
            $"network inspect {QuotePositionalArgument(networkId, nameof(networkId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Network>.Fail(
              ErrorOrDefault(result, "Network inspect failed"), FailureCode(result.Error, ErrorCodes.Network.InspectFailed),
              CreateErrorContext(context, "InspectNetwork", result), result.ExitCode);

        var network = ParseNetworkInspect(result.Output);
        if (string.IsNullOrEmpty(network.Id) && string.IsNullOrEmpty(network.Name))
          return CommandResponse<Network>.Fail(
              $"Network '{networkId}' was not found", ErrorCodes.Network.NotFound,
              CreateErrorContext(context, "InspectNetwork", result), result.ExitCode);
        return CommandResponse<Network>.Ok(network);
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
    public async Task<CommandResponse<NetworkPruneResult>> PruneAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, "network prune -f", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<NetworkPruneResult>.Fail(
              ErrorOrDefault(result, "Network prune failed"), FailureCode(result.Error, ErrorCodes.Network.PruneFailed),
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
        return CommandResponse<NetworkPruneResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Network.PruneFailed));
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
        Id = token.GetStringOrDefault("id")
             ?? token.GetStringOrDefault("ID")
             ?? token.GetStringOrDefault("Id"),
        Name = token.GetStringOrDefault("Name") ?? token.GetStringOrDefault("name"),
        Driver = token.GetStringOrDefault("Driver") ?? token.GetStringOrDefault("driver"),
        Scope = token.GetStringOrDefault("Scope") ?? token.GetStringOrDefault("scope"),
        IPv6 = token.GetBoolOrDefault("IPv6Enabled") || token.GetBoolOrDefault("ipv6_enabled"),
        Internal = token.GetBoolOrDefault("Internal") || token.GetBoolOrDefault("internal"),
        Labels = ReadStringDictionary(token, "labels", "Labels"),
        Containers = ParseContainers(token)
      };
    }

    private static Dictionary<string, string> ReadStringDictionary(JsonElement token, string first, string second)
    {
      var value = token.GetStringDictionary(first);
      return value.Count == 0 ? token.GetStringDictionary(second) : value;
    }

    private static Dictionary<string, NetworkedContainer> ParseContainers(JsonElement token)
    {
      var result = new Dictionary<string, NetworkedContainer>();
      var containers = token.Prop("Containers", "containers");
      if (containers?.ValueKind != JsonValueKind.Object)
        return result;

      foreach (var item in containers.Value.EnumerateObject())
      {
        if (item.Value.ValueKind != JsonValueKind.Object)
          continue;

        result[item.Name] = ParseContainer(item.Value);
      }

      return result;
    }

    private static NetworkedContainer ParseContainer(JsonElement token)
    {
      var container = new NetworkedContainer
      {
        Name = token.GetStringOrDefault("Name") ?? token.GetStringOrDefault("name"),
        EndpointID = token.GetStringOrDefault("EndpointID") ?? token.GetStringOrDefault("endpoint_id"),
        MacAddress = token.GetStringOrDefault("MacAddress") ?? token.GetStringOrDefault("mac_address"),
        IPv4Address = token.GetStringOrDefault("IPv4Address") ?? token.GetStringOrDefault("ipv4_address"),
        IPv6Address = token.GetStringOrDefault("IPv6Address") ?? token.GetStringOrDefault("ipv6_address")
      };

      if (string.IsNullOrEmpty(container.EndpointID))
        container.EndpointID = token.GetStringOrDefault("endpointID");

      ApplyFirstInterface(container, token);
      return container;
    }

    private static void ApplyFirstInterface(NetworkedContainer container, JsonElement token)
    {
      var interfaces = token.Prop("interfaces", "Interfaces");
      if (interfaces?.ValueKind != JsonValueKind.Object)
        return;

      foreach (var iface in interfaces.Value.EnumerateObject())
      {
        if (iface.Value.ValueKind != JsonValueKind.Object)
          return;

        container.MacAddress ??= iface.Value.GetStringOrDefault("mac_address")
                                 ?? iface.Value.GetStringOrDefault("MacAddress");
        ApplyFirstSubnet(container, iface.Value);
        return; // ponytail: one interface is enough for network membership; expand if Podman exposes multi-NIC needs.
      }
    }

    private static void ApplyFirstSubnet(NetworkedContainer container, JsonElement iface)
    {
      var subnets = iface.Prop("subnets", "Subnets");
      if (subnets?.ValueKind != JsonValueKind.Array)
        return;

      foreach (var subnet in subnets.Value.EnumerateArray())
      {
        var ip = subnet.GetStringOrDefault("ipnet") ?? subnet.GetStringOrDefault("IPNet");
        if (string.IsNullOrEmpty(ip))
          continue;

        if (ip.Contains(':'))
          container.IPv6Address ??= ip;
        else
          container.IPv4Address ??= ip;
      }
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
