using FluentDocker.Model.Containers;

namespace FluentDocker.Executors.Parsers
{
  public sealed class ProcessExitAwareResponseParser : IProcessResponseParser<string>
  {
    public CommandResponse<string> Response { get; private set; }

    public IProcessResponse<string> Process(ProcessExecutionResult response)
    {
      Response = response.ToResponse(response.ExitCode == 0, string.Empty, $"ExitCode={response.ExitCode}");
      return this;
    }
  }
}
