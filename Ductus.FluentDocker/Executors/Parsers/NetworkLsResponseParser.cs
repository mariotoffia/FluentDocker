using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Networks;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class NetworkLsResponseParser : IProcessResponseParser<IList<NetworkRow>>
  {
    public const string Format = "{{.ID}};{{.Name}};{{.Driver}};{{.Scope}};{{.IPv6}};{{.Internal}};{{.CreatedAt}}";

    public CommandResponse<IList<NetworkRow>> Response { get; private set; }

    public IProcessResponse<IList<NetworkRow>> Process(ProcessExecutionResult response)
    {
      if (response.ExitCode != 0)
      {
        Response = response.ToErrorResponse((IList<NetworkRow>)new List<NetworkRow>());
        return this;
      }

      if (string.IsNullOrEmpty(response.StdOut))
      {
        Response = response.ToResponse(false, "No response", (IList<NetworkRow>)new List<NetworkRow>());
        return this;
      }

      var result = new List<NetworkRow>();

      foreach (var row in response.StdOutAsArry)
      {
        var items = row.Split(';');
        if (items.Length < 4)
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
          var split = items[6].Split(" ".ToCharArray());
          var normalizedStr = $"{split[0]} {split[1]} {split[2].Insert(3, ":")}";
          DateTime.TryParse(normalizedStr, out created);
        }

        result.Add(new NetworkRow
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

      Response = response.ToResponse(true, string.Empty, (IList<NetworkRow>)result);
      return this;
    }
  }
}
