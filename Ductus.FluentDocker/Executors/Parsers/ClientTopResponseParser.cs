using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public class ClientTopResponseParser : IProcessResponseParser<Processes>
  {
    public CommandResponse<Processes> Response { get; private set; }

    public IProcessResponse<Processes> Process(ProcessExecutionResult response)
    {
      var rows = response.StdOutAsArray;
      if (response.ExitCode != 0 || 0 == rows.Length)
      {
        Response = response.ToErrorResponse(new Processes { Columns = new List<string>(), Rows = new List<ProcessRow>() });
        return this;
      }

      var columns = ParseColumns(rows[0]);
      if (0 == columns.Count)
      {
        Response = response.ToResponse(false, "No Process Columns Found",
          new Processes { Columns = new List<string>(), Rows = new List<ProcessRow>() });
        return this;
      }

      var processes = new Processes { Columns = ColumnSplit(columns, rows[0]), Rows = new List<ProcessRow>() };
      for (var i = 1; i < rows.Length; i++)
      {
        var row = ColumnSplit(columns, rows[i]);
        processes.Rows.Add(ProcessRow.ToRow(processes.Columns, row));
      }

      Response = response.ToResponse(true, string.Empty, processes);
      return this;
    }

    private IList<string> ColumnSplit(IList<int> columns, string row)
    {
      var list = new List<string>();
      for (var i = 0; i < columns.Count; i++)
      {
        if (i == columns.Count - 1)
        {
          list.Add(row.Substring(columns[i]).Trim());
          break;
        }

        list.Add(row.Substring(columns[i], columns[i + 1] - columns[i]).Trim());
      }

      return list;
    }

    private IList<int> ParseColumns(string row)
    {
      var list = new List<int>();
      if (string.IsNullOrWhiteSpace(row))
      {
        return list;
      }

      var inText = false;
      for (var i = 0; i < row.Length; i++)
      {
        if (row[i] != ' ')
        {
          if (!inText)
          {
            inText = true;
            list.Add(i);
          }

          continue;
        }

        inText = false;
      }

      // TODO: Hack to handle windows container - when other lang this will break!
      if (list.Count == 6 && row.IndexOf("Private Working Set", StringComparison.Ordinal) != -1)
      {
        list.RemoveAt(4);
        list.RemoveAt(4);
      }

      return list;
    }
  }
}
