﻿using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class StringListResponseParser : IProcessResponseParser<IList<string>>
  {
    public CommandResponse<IList<string>> Response { get; private set; }

    public IProcessResponse<IList<string>> Process(ProcessExecutionResult response)
    {
      if (response.ExitCode != 0)
      {
        Response = response.ToErrorResponse((IList<string>)new List<string>());
        return this;
      }

      Response = response.ToResponse(true, string.Empty, (IList<string>)new List<string>(response.StdOutAsArry));
      return this;
    }
  }
}
