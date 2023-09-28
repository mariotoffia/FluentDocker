using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Networks;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class NetworkLsResponseParser : IProcessResponseParser<IList<NetworkRow>>
  {
    public const string Format = "{{.ID}};{{.Name}}";

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

      foreach (var row in response.StdOutAsArray)
      {
        var items = row.Split(';');
        if (items.Length < 2)
          continue;

        result.Add(new NetworkRow
        {
          Id = items[0],
          Name = items[1],
        });
      }

      Response = response.ToResponse(true, string.Empty, (IList<NetworkRow>)result);
      return this;
    }
  }
}
