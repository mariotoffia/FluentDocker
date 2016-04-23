using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Model.Builders
{
  public sealed class BuildCommand
  {
    public CommandType Command { get; set; }
    public IList<string> Lines { get; set; }

    public override string ToString()
    {
      return $"{Command.ToCommandString()} " + string.Join($"\\{Environment.NewLine}", Lines);
    }
  }
}