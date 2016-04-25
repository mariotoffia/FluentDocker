using System.Collections.Generic;
using System.Text;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Model.Builders
{
  public sealed class FileBuilderConfig
  {
    public string DockerFileString { get; set; }
    public TemplateString UseFile { get; set; }
    public string From { get; set; }
    public string Maintainer { get; set; }
    public IList<ICommand> AddRunCommands { get; } = new List<ICommand>();
    public string Workdir { get; set; }
    public IList<int> Expose { get; set; }
    public IList<string> Command { get; } = new List<string>();

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.Append("FROM ").AppendLine(From);
      if (!string.IsNullOrWhiteSpace(Maintainer))
      {
        sb.Append("MAINTAINER ").AppendLine(Maintainer);
      }

      if (0 != AddRunCommands.Count)
      {
        foreach (var command in AddRunCommands)
        {
          sb.AppendLine(command.ToString());
        }
      }

      if (!string.IsNullOrWhiteSpace(Workdir))
      {
        sb.Append("WORKDIR ").AppendLine(Workdir);
      }

      if (null != Expose && 0 != Expose.Count)
      {
        sb.Append("EXPOSE ").AppendLine(string.Join(" ", Expose));
      }

      if (0 != Command.Count)
      {
        sb.Append("CMD [");
        for (var i = 0; i < Command.Count; i++)
        {
          sb.Append('"').Append(Command[i]).Append('"').Append(i == Command.Count - 1 ? string.Empty : ",");
        }
        sb.AppendLine("]");
      }

      return sb.ToString();
    }
  }
}