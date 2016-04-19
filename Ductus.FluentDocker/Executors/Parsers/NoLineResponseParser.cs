using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class NoLineResponseParser : IProcessResponseParser<string>
  {
    public CommandResponse<string> Response { get; private set; }
    public IProcessResponse<string> Process(ProcessExecutionResult response)
    {
      Response = response.ExitCode != 0
        ? response.ToErrorResponse(string.Empty)
        : response.ToResponse(true, string.Empty, string.Empty);
      return this;
    }
  }
}
