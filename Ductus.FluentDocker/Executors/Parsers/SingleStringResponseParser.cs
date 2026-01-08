using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class SingleStringResponseParser : IProcessResponseParser<string>
  {
    public CommandResponse<string> Response { get; private set; }

    public IProcessResponse<string> Process(ProcessExecutionResult response)
    {
      // Check for command failure first
      if (response.ExitCode != 0)
      {
        var errorMessage = !string.IsNullOrWhiteSpace(response.StdErr) 
          ? response.StdErr.Trim() 
          : $"Command failed with exit code {response.ExitCode}";
        Response = response.ToResponse(false, errorMessage, string.Empty);
        return this;
      }

      var arr = response.StdOutAsArray;
      if (arr.Length == 0)
      {
        var errorMessage = !string.IsNullOrWhiteSpace(response.StdErr)
          ? response.StdErr.Trim()
          : "No output received from command";
        Response = response.ToResponse(false, errorMessage, string.Empty);
        return this;
      }

      Response = response.ToResponse(true, string.Empty, arr[0]);
      return this;
    }
  }
}
