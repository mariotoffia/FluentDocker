namespace Ductus.FluentDocker.Executors
{
  public interface IProcessResponse<out T>
  {
    T Response { get; }
  }

  public interface IProcessResponseParser<out T> : IProcessResponse<T>
  {
    IProcessResponse<T> Process(string response);
  }
}
