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

    public string User { get; internal set; }
    public long Pid { get; internal set; }
    public long ProcessPid { get; internal set; }
    public string Command { get; internal set; }
    public string Tty { get; internal set; }
    public TimeSpan Time { get; internal set; }
    public TimeSpan Started { get; internal set; }
    public string Status { get; internal set; }
    public float PercentCpuUtilization { get; internal set; }
    public TimeSpan Cpu { get; private set; }
    public float PercentMemoryUtilization { get; internal set; }
    public IList<string> FullRow { get; internal set; }

    internal static ProcessRow ToRow(IList<string> columns, IList<string> fullRow)
    {
      var row = new ProcessRow {FullRow = fullRow};

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
          case UserConst:
          case UidConst:
            row.User = fullRow[i];
            break;
          case PidConst:
            row.Pid = long.Parse(fullRow[i]);
            break;
          case PpidConst:
            row.ProcessPid = long.Parse(fullRow[i]);
            break;
          case StartConst:
          case StartTimeConst:
            row.Started = TimeSpan.Parse(fullRow[i]);
            break;
          case TimeConst:
            row.Time = TimeSpan.Parse(fullRow[i]);
            break;
          case TerminalConst:
            row.Tty = fullRow[i];
            break;
          case StatConst:
            row.Status = fullRow[i];
            break;
          case CpuTime:
            if (TimeSpan.TryParse(fullRow[i], out var cpuTime)) row.Cpu = cpuTime;
            break;
          case PercentCpuConst:
            row.PercentCpuUtilization = float.Parse(fullRow[i], CultureInfo.InvariantCulture.NumberFormat);
            break;
          case PercentMemoryConst:
            row.PercentMemoryUtilization = float.Parse(fullRow[i], CultureInfo.InvariantCulture.NumberFormat);
            break;
        }
      }

      return row;
    }
  }
}
