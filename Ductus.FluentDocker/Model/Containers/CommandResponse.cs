using System;
using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Containers
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
    public IList<string> Log { get; }
    public string Error { get; private set; }
    public T Data { get; }

    public override string ToString()
    {
      if (null == Log)
      {
        return base.ToString();
      }

      return string.Join(Environment.NewLine, Log);
    }
  }
}