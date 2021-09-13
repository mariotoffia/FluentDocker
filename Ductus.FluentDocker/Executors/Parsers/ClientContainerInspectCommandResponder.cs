using System.Text;
using Ductus.FluentDocker.Model.Containers;
using Newtonsoft.Json;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class ClientContainerInspectCommandResponder : IProcessResponseParser<Container>
  {
    public CommandResponse<Container> Response { get; private set; }

    public IProcessResponse<Container> Process(ProcessExecutionResult response)
    {
      if (string.IsNullOrEmpty(response.StdOut))
      {
        Response = response.ToResponse(false, "Empty response", new Container());
        return this;
      }

      if (response.ExitCode != 0)
      {
        Response = response.ToErrorResponse(new Container());
        return this;
      }


      var arr = response.StdOutAsArray;
      var sb = new StringBuilder();
      for (var i = 1; i < arr.Length - 1; i++)
      {
        sb.AppendLine(arr[i]);
      }

      var container = sb.ToString();
      var obj = JsonConvert.DeserializeObject<Container>(container);

      obj.Name = TrimIfBeginsWithSlash(obj.Name);

      Response = response.ToResponse(true, string.Empty, obj);
      return this;
    }

    private static string TrimIfBeginsWithSlash(string name)
    {
      if (!string.IsNullOrEmpty(name) && name.StartsWith("/"))
        return name.Substring(1);
      return name;
    }
  }
}
