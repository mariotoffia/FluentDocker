using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Images;
using Ductus.FluentDocker.Extensions;
using System;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class ImageRmResponseParser : IProcessResponseParser<IList<DockerRmImageRowResponse>>
  {
    public CommandResponse<IList<DockerRmImageRowResponse>> Response { get; private set; }
    public IProcessResponse<IList<DockerRmImageRowResponse>> Process(ProcessExecutionResult response)
    {
      if (response.ExitCode != 0)
      {
        Response = response.ToErrorResponse((IList<DockerRmImageRowResponse>)new List<DockerRmImageRowResponse>());
        return this;
      }

      var list = new List<DockerRmImageRowResponse>();
      foreach (var row in response.StdOutAsArray)
      {
        var items = row.Split(new string[]{": "},1,StringSplitOptions.RemoveEmptyEntries);
        if (items.Length != 2)
        {
          continue;
        }


        list.Add(new DockerRmImageRowResponse
        {
          Id = items[1].Contains("sha") ? items[1].ToPlainId() : items[1],
          Command = items[0]
        });
      }

      Response = response.ToResponse(true, string.Empty, (IList<DockerRmImageRowResponse>)list);
      return this;
    }
  }
}
