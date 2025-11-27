using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors
{
  public sealed class ProcessExecutionResult
  {
    internal ProcessExecutionResult(string process, string stdOut, string stdErr, int exitCode)
    {
      Command = process;
      StdOut = stdOut;
      StdErr = stdErr;
      ExitCode = exitCode;
    }

    public string Command { get; }
    public string StdOut { get; }
    public string StdErr { get; }
    public int ExitCode { get; }
    [Obsolete("Please use the properly spelled `StdOutAsArray` method instead.")]
    public string[] StdOutAsArry => StdOutAsArray;
    public string[] StdOutAsArray => StdOut.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
    [Obsolete("Please use the properly spelled `StdErrAsArray` method instead.")]
    public string[] StdErrAsArry => StdErrAsArray;
    public string[] StdErrAsArray => StdErr.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

    public CommandResponse<T> ToResponse<T>(bool success, string error, T data)
    {
      var log = new List<string>(StdOutAsArray);
      if (!string.IsNullOrEmpty(StdErr))
      {
        log.AddRange(StdErrAsArray);
      }

      return new CommandResponse<T>(success, log, error, data);
    }

    public CommandResponse<T> ToErrorResponse<T>(T data)
    {
      var err = StdErr;
      if (string.IsNullOrWhiteSpace(err))
      {
        err = $"Error when executing command, exit code {ExitCode}";
      }

      return ToResponse(false, err, data);
    }
  }
}
