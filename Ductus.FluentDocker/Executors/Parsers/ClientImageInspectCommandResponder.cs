using System.Text;
using Ductus.FluentDocker.Model.Containers;
using Newtonsoft.Json;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class ClientImageInspectCommandResponder : IProcessResponseParser<ImageConfig>
  {
    public CommandResponse<ImageConfig> Response { get; private set; }

    public IProcessResponse<ImageConfig> Process(ProcessExecutionResult response)
    {
      if (string.IsNullOrEmpty(response.StdOut))
      {
        Response = response.ToResponse(false, "Empty response", new ImageConfig());
        return this;
      }

      if (response.ExitCode != 0)
      {
        Response = response.ToErrorResponse(new ImageConfig());
        return this;
      }


      var arr = response.StdOutAsArry;
      var sb = new StringBuilder();
      for (var i = 1; i < arr.Length - 1; i++)
      {
        sb.AppendLine(arr[i]);
      }

      var container = sb.ToString();
      Response = response.ToResponse(true, string.Empty, JsonConvert.DeserializeObject<ImageConfig>(container));
      return this;
    }
  }
}