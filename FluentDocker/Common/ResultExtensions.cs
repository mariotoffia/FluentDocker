using System;
using System.Collections.Generic;

namespace FluentDocker.Common
{
  /// <summary>
  /// Extension methods for creating <see cref="Result{T}"/> instances from data values.
  /// </summary>
  public static class ResultExtensions
  {
    private static readonly string[] LineSeparators = ["\n", "\r\n"];

    /// <summary>Wraps the data in a successful <see cref="Result{T}"/>.</summary>
    /// <typeparam name="T">The data type.</typeparam>
    /// <param name="data">The result value.</param>
    /// <param name="log">Optional log output.</param>
    /// <returns>A successful result containing <paramref name="data"/>.</returns>
    public static Result<T> ToSuccess<T>(this T data, string log = null)
    {
      return new Result<T>(true, data, log ?? string.Empty, string.Empty);
    }

    /// <summary>Wraps the data in a successful <see cref="Result{T}"/> with log entries.</summary>
    /// <typeparam name="T">The data type.</typeparam>
    /// <param name="data">The result value.</param>
    /// <param name="log">Log entries to join into a single string.</param>
    /// <returns>A successful result containing <paramref name="data"/>.</returns>
    public static Result<T> ToSuccess<T>(this T data, IList<string> log)
    {
      return new Result<T>(true, data, log.FromLog(), string.Empty);
    }

    /// <summary>Wraps the data in a failed <see cref="Result{T}"/>.</summary>
    /// <typeparam name="T">The data type.</typeparam>
    /// <param name="data">The result value (may be default).</param>
    /// <param name="error">The error message.</param>
    /// <param name="log">Optional log output.</param>
    /// <returns>A failed result containing the error.</returns>
    public static Result<T> ToFailure<T>(this T data, string error, string log = null)
    {
      return new Result<T>(false, data, log ?? string.Empty, error);
    }

    /// <summary>Wraps the data in a failed <see cref="Result{T}"/> with log entries.</summary>
    /// <typeparam name="T">The data type.</typeparam>
    /// <param name="data">The result value (may be default).</param>
    /// <param name="error">The error message.</param>
    /// <param name="log">Log entries to join into a single string.</param>
    /// <returns>A failed result containing the error.</returns>
    public static Result<T> ToFailure<T>(this T data, string error, IList<string> log)
    {
      return new Result<T>(false, data, log.FromLog(), error);
    }

    /// <summary>Joins a list of log entries into a single newline-delimited string.</summary>
    /// <param name="entries">The log entries to join.</param>
    /// <returns>A joined string, or empty if <paramref name="entries"/> is null or empty.</returns>
    public static string FromLog(this IList<string> entries)
    {
      if (null == entries || 0 == entries.Count)
      {
        return string.Empty;
      }

      return string.Join(Environment.NewLine, entries);
    }

    /// <summary>Splits a log string into individual entries by newline separators.</summary>
    /// <param name="log">The log string to split.</param>
    /// <returns>An array of non-empty log entries.</returns>
    public static string[] ToEntires(this string log)
    {
      if (string.IsNullOrEmpty(log))
      {
        return [];
      }

      return log.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
    }
  }
}
