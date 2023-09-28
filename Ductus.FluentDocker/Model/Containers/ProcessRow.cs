using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class ProcessRow
  {
    private const string UserConst = "USER";
    private const string UidConst = "UID";
    private const string PidConst = "PID";
    private const string PpidConst = "PPID";
    private const string C = "C";
    private const string TerminalConst = "TTY";
    private const string StartTimeConst = "STIME";
    private const string TimeConst = "TIME";
    private const string CommandConst = "COMMAND";
    private const string CmdConst = "CMD";
    private const string PercentCpuConst = "%CPU";
    private const string CpuTime = "CPU";
    private const string PercentMemoryConst = "%MEM";
    private const string VszConst = "VSZ";
    private const string RssConst = "RSS";
    private const string StatConst = "STAT";
    private const string StartConst = "START";
    private const string CmdConstWin = "Name";

    public string Command { get; internal set; }
    public IList<string> FullRow { get; internal set; }

    internal static ProcessRow ToRow(IList<string> columns, IList<string> fullRow)
    {
      var row = new ProcessRow { FullRow = fullRow };

      for (var i = 0; i < columns.Count; i++)
      {
        var column = columns[i];

        switch (column)
        {
          case CmdConst:
          case CommandConst:
          case CmdConstWin:
            row.Command = fullRow[i];
            break;
        }
      }

      return row;
    }
  }
}
