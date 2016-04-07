using System.Collections.Generic;

namespace Ductus.FluentDocker.Model
{
  public sealed class CommandResponse
  {
    public CommandResponse(bool success, IList<string> log)
    {
      Success = success;
      Log = log;
    }

    public bool Success { get; private set; }
    public IList<string> Log { get; private set; }
  }
}