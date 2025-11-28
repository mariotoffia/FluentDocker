using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Extensions;
using FluentDocker.Model.Commands;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Networks;

namespace FluentDocker.Commands
{
  /// <summary>
  /// Docker network commands.
  /// </summary>
  /// <remarks>
  /// This class is deprecated. Use the INetworkDriver interface from the FluentDocker.Drivers namespace instead.
  /// The Driver layer provides async operations, better error handling, and support for multiple container runtimes.
  /// </remarks>
  [System.Obsolete("Use INetworkDriver from FluentDocker.Drivers namespace instead. Will be removed in v4.0.0.")]
  public static class Network
  {
    #region New struct-based command methods

    /// <summary>
    /// Lists networks using command args struct.
    /// </summary>
    public static CommandResponse<IList<NetworkRow>> NetworkLsCommand(this DockerUri host, NetworkLsCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      if (!options.Contains("--format"))
        options += $" --format \"{NetworkLsResponseParser.Format}\"";

      return
        new ProcessExecutor<NetworkLsResponseParser, IList<NetworkRow>>(
          "docker".ResolveBinary(),
          $"{certArgs} network ls {options}").Execute();
    }

    /// <summary>
    /// Connects a container to a network using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> NetworkConnectCommand(this DockerUri host, NetworkConnectCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} network connect {options} {args.Network} {args.Container}").Execute();
    }

    /// <summary>
    /// Disconnects a container from a network using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> NetworkDisconnectCommand(this DockerUri host, NetworkDisconnectCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} network disconnect {options} {args.Network} {args.Container}").Execute();
    }

    /// <summary>
    /// Removes networks using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> NetworkRmCommand(this DockerUri host, NetworkRmCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var networks = args.Networks != null ? string.Join(" ", args.Networks) : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} network rm {options} {networks}").Execute();
    }

    /// <summary>
    /// Inspects networks using command args struct.
    /// </summary>
    public static CommandResponse<NetworkConfiguration> NetworkInspectCommand(this DockerUri host, NetworkInspectCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var networks = args.Networks != null ? string.Join(" ", args.Networks) : "";

      return
        new ProcessExecutor<NetworkInspectResponseParser, NetworkConfiguration>(
          "docker".ResolveBinary(),
          $"{certArgs} network inspect {options} {networks}").Execute();
    }

    /// <summary>
    /// Creates a network using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> NetworkCreateCommand(this DockerUri host, NetworkCreateCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} network create {options} {args.Name}").Execute();
    }

    /// <summary>
    /// Prunes unused networks using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> NetworkPruneCommand(this DockerUri host, NetworkPruneCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} network prune {options}").Execute();
    }

    #endregion

    #region Existing methods (backward compatible)
    public static CommandResponse<IList<NetworkRow>> NetworkLs(this DockerUri host,
      ICertificatePaths certificates = null, params string[] filters)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var options = $" --no-trunc --format \"{NetworkLsResponseParser.Format}\"";

      if (null != filters && 0 != filters.Length)
        options = filters.Aggregate(options, (current, filter) => current + $" --filter={filter}");

      return
        new ProcessExecutor<NetworkLsResponseParser, IList<NetworkRow>>(
          "docker".ResolveBinary(),
          $"{args} network ls {options}").Execute();
    }

    public static CommandResponse<IList<string>> NetworkConnect(this DockerUri host, string container, string network,
      string[] alias = null, string ipv4 = null, string ipv6 = null,
      ICertificatePaths certificates = null, params string[] links)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var options = new StringBuilder();
      options.OptionIfExists("--alias=", alias)
        .OptionIfExists("--ip ", ipv4)
        .OptionIfExists("--ipv6 ", ipv6)
        .OptionIfExists("--link=", links);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} network connect {options} {network} {container}").Execute();
    }

    public static CommandResponse<IList<string>> NetworkDisconnect(this DockerUri host, string container,
      string network,
      bool force = false,
      ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var options = string.Empty;
      if (force)
        options += " -f";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} network disconnect {options} {network} {container}").Execute();
    }

    public static CommandResponse<NetworkConfiguration> NetworkInspect(this DockerUri host,
      ICertificatePaths certificates = null, params string[] network)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var options = string.Empty;
      options += string.Join(" ", network);
      return
        new ProcessExecutor<NetworkInspectResponseParser, NetworkConfiguration>(
          "docker".ResolveBinary(),
          $"{args} network inspect {options}").Execute();
    }

    public static CommandResponse<IList<string>> NetworkRm(this DockerUri host,
      ICertificatePaths certificates = null, params string[] network)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} network rm {string.Join(" ", network)}").Execute();
    }

    public static CommandResponse<IList<string>> NetworkCreate(this DockerUri host, string network,
      NetworkCreateParams prms = null, ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} network create {prms} {network}").Execute();
    }

    #endregion
  }
}
