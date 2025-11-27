using System.Collections.Generic;
using System.Text;

namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class Processes
  {
    public IList<string> Columns { get; set; }
    public IList<ProcessRow> Rows { get; set; }

    public override string ToString()
    {
      if (null == Columns)
      {
        return string.Empty;
      }

      var sb = new StringBuilder();

      foreach (var column in Columns)
      {
        sb.Append(column).Append("\t");
      }

      sb.AppendLine();
      sb.AppendLine("-----------------------------------------------------");

      if (null == Rows)
      {
        sb.AppendLine("No Processes");
        return sb.ToString();
      }

      foreach (var row in Rows)
      {
        foreach (var column in row.FullRow)
        {
          sb.Append(column).Append("\t");
        }

        sb.AppendLine();
      }

      return sb.ToString();
    }
  }
}
