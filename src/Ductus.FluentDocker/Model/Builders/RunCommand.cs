using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Model.Builders
{
  public sealed class RunCommand : ICommand
  {
    public IList<TemplateString> Lines { get; set; }

    public override string ToString()
    {
      return "RUN " + string.Join($"\\{Environment.NewLine}", Lines);
    }
  }
}