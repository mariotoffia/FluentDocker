using System;
using System.Collections.Generic;
using System.Text;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Model.Builders
{
  public sealed class FileBuilderConfig
  {
    public string DockerFileString { get; set; }
    public TemplateString UseFile { get; set; }
    public IList<ICommand> Commands { get; } = new List<ICommand>();

    public override string ToString()
    {
      var sb = new StringBuilder();
      foreach(var cmd in Commands)
      {
        sb.AppendLine(cmd.ToString());
      }
      return sb.ToString();
    }
  }
}