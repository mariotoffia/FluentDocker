using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors
{
  public interface IProcessResponse<T>
  {
    CommandResponse<T> Response { get; }
  }

  public interface IProcessResponseParser<T> : IProcessResponse<T>
  {
    IProcessResponse<T> Process(ProcessExecutionResult response);
  }
}