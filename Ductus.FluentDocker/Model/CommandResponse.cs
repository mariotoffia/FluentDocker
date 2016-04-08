using System.Collections.Generic;

namespace Ductus.FluentDocker.Model
{
  public sealed class CommandResponse<T>
  {
    public CommandResponse(bool success, IList<string> log, string error = "", T data = default(T))
    {
      Success = success;
      Log = log;
      Error = error;
      Data = data;
    }

    public bool Success { get; private set; }
    public IList<string> Log { get; private set; }
    public string Error { get; private set; }
    public T Data { get; }
  }
}