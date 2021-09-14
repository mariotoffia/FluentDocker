using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class SingleStringResponseParser : IProcessResponseParser<string>
  {
    public CommandResponse<string> Response { get; private set; }

    public IProcessResponse<string> Process(ProcessExecutionResult response)
    {
      var arr = response.StdOutAsArray;

      Response = arr.Length == 0
        ? response.ToResponse(false, "No line", string.Empty)
        : response.ToResponse(true, string.Empty, arr[0]);

      return this;
    }
  }
}
