using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Images;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class ClientImagesResponseParser : IProcessResponseParser<IList<DockerImageRowResponse>>
  {
    public CommandResponse<IList<DockerImageRowResponse>> Response { get; private set; }
    public IProcessResponse<IList<DockerImageRowResponse>> Process(ProcessExecutionResult response)
    {
      if (response.ExitCode != 0)
      {
        Response = response.ToErrorResponse((IList<DockerImageRowResponse>) new List<DockerImageRowResponse>());
        return this;
      }

      var list = new List<DockerImageRowResponse>();
      foreach (var row in response.StdOutAsArry)
      {
        var items = row.Split(';');
        if (items.Length != 3)
        {
          continue;
        }

        list.Add(new DockerImageRowResponse
        {
          Id = items[0].ToPlainId(),
          Name = items[1],
          Tags = new[] {items[2]}
        });
      }

      Response = response.ToResponse(true,string.Empty,(IList<DockerImageRowResponse>)list);
      return this;
    }
  }
}
