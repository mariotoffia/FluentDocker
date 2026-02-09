using FluentDocker.Model.Containers;

namespace FluentDocker.Executors
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
