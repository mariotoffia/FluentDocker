using System.Collections.Generic;
using System.Linq;
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
    public IList<RunCommand> BuildCommands { get; } = new List<RunCommand>();
    public IList<AddCommand> AddCommands { get; } = new List<AddCommand>();
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

      if (0 != BuildCommands.Count)
      {
        foreach (var command in BuildCommands)
        {
          sb.AppendLine(command.ToString());
        }
      }

      if (0 != AddCommands.Count)
      {
        foreach (var cmd in AddCommands)
        {
          sb.AppendLine(cmd.ToString());
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
        sb.Append("CMD [\"").Append(Command[0]);
        if (Command.Count > 1)
        {
          sb.Append(string.Join("\", \"", Command.Skip(1)));
        }
        sb.AppendLine("\"]");
      }

      return sb.ToString();
    }
  }
}