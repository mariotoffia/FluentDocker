using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Networks;

namespace Ductus.FluentDocker.Commands
{
  public static class Network
  {
    public static CommandResponse<IList<NetworkRow>> NetworkLs(this DockerUri host,
      ICertificatePaths certificates = null, params string[] filters)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var options =
        " --no-trunc --format \"{{.ID}};{{.Name}};{{.Driver}};{{.Scope}};{{.IPv6}};{{.Internal}};{{.CreatedAt}}\"";

      if (null != filters && 0 != filters.Length)
        options = filters.Aggregate(options, (current, filter) => current + $" --filter={filter}");

      var response =
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} network ls {options}").Execute();

      var list = new List<NetworkRow>();
      var res = new CommandResponse<IList<NetworkRow>>(response.Success, response.Log, response.Error, list);
      if (!response.Success)
        return res;

      foreach (var row in response.Data)
      {
        var items = row.Split(';');
        if (null == items || items.Length < 4)
          continue;

        var created = DateTime.MinValue;
        var ipv6 = false;
        var intern = false;

        if (items.Length > 4)
          bool.TryParse(items[4], out ipv6);
        if (items.Length > 5)
          bool.TryParse(items[5], out intern);
        if (items.Length > 6)
        {
          DateTime.TryParse(items[6].Substring(0, items[6].IndexOf('+')), out created);
          created = DateTime.SpecifyKind(created, DateTimeKind.Utc);
        }

        list.Add(new NetworkRow
        {
          Id = items[0],
          Name = items[1],
          Driver = items[2],
          Scope = items[3],
          IPv6 = ipv6,
          Internal = intern,
          Created = created
        });
      }

      return res;
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
        new ProcessExecutor<NetworkLsResponseParser, NetworkConfiguration>(
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
  }
}