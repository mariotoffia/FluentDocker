using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class ClientDiffResponseParser : IProcessResponseParser<IList<Diff>>
  {
    public CommandResponse<IList<Diff>> Response { get; private set; }

    public IProcessResponse<IList<Diff>> Process(ProcessExecutionResult response)
    {
      var rows = response.StdOutAsArry;
      if (0 != response.ExitCode)
      {
        Response = response.ToErrorResponse((IList<Diff>) new List<Diff>());
        return this;
      }

      Response = response.ToResponse(true, string.Empty,
        (IList<Diff>) rows.Select(row => new Diff {Type = ToDiffType(row[0]), Item = row.Substring(2).Trim()}).ToList());

      return this;
    }

    private static DiffType ToDiffType(char type)
    {
      switch (type)
      {
        case 'A':
          return DiffType.Added;
        case 'U':
          return DiffType.Updated;
        case 'R':
          return DiffType.Removed;
        case 'C':
          return DiffType.Created;
        default:
          throw new NotImplementedException($"The diff type {type} is not implemented");
      }
    }
  }
}
