using System;
using System.Collections.Generic;

namespace Ductus.FluentDocker.Common
{
  public static class ResultExtensions
  {
    public static Result<T> ToSuccess<T>(this T data, string log = null)
    {
      return new Result<T>(true, data, log ?? string.Empty, string.Empty);
    }

    public static Result<T> ToSuccess<T>(this T data, IList<string> log)
    {
      return new Result<T>(true, data, log.FromLog(), string.Empty);
    }
    public static Result<T> ToFailure<T>(this T data, string error, string log = null)
    {
      return new Result<T>(false, data, log ?? string.Empty, error);
    }

    public static Result<T> ToFailure<T>(this T data, string error, IList<string> log)
    {
      return new Result<T>(false, data, log.FromLog(), error);
    }

    public static string FromLog(this IList<string> entries)
    {
      if (null == entries || 0 == entries.Count)
      {
        return string.Empty;
      }

      return string.Join(Environment.NewLine, entries);
    }

    public static string[] ToEntires(this string log)
    {
      if (string.IsNullOrEmpty(log))
      {
        return new string[0];
      }

      return log.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
    }
  }
}
