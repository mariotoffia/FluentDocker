using Ductus.FluentDocker.Model;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class BaseInfoResponseParser : IProcessResponseParser<DockerInfoBase>
  {
    public CommandResponse<DockerInfoBase> Response { get; private set; }

    public IProcessResponse<DockerInfoBase> Process(ProcessExecutionResult response)
    {
      if (response.ExitCode != 0)
      {
        Response =
          response.ToErrorResponse(new DockerInfoBase
          {
            ClientApiVersion = string.Empty,
            ClientVersion = string.Empty,
            ServerApiVersion = string.Empty,
            ServerVersion = string.Empty
          });
        return this;
      }

      var s = response.StdOut.TrimEnd('\r', '\n').Split(';');
      if (s.Length != 5)
      {
        Response =
          response.ToErrorResponse(new DockerInfoBase
          {
            ClientApiVersion = string.Empty,
            ClientVersion = string.Empty,
            ServerApiVersion = string.Empty,
            ServerVersion = string.Empty
          });
        return this;
      }


      Response = response.ToResponse(true, string.Empty, new DockerInfoBase
      {
        ClientApiVersion = s[3],
        ClientVersion = s[2],
        ServerApiVersion = s[1],
        ServerVersion = s[0],
        ServerOs = s[4]
      });
      return this;
    }
  }
}
