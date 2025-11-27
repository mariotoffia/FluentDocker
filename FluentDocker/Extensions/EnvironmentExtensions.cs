using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Extensions
{
  public static class EnvironmentExtensions
  {
    public static Tuple<string, string> Extract(this string envExpression)
    {
      if (string.IsNullOrWhiteSpace(envExpression))
        return null;

      var idx = envExpression.IndexOf('=');
      if (-1 == idx)
        return new Tuple<string, string>(envExpression.Trim(), string.Empty);

      if (idx == envExpression.Length - 1)
        return new Tuple<string, string>(envExpression.Substring(0, idx).Trim(), string.Empty);

      return new Tuple<string, string>(envExpression.Substring(0, idx).Trim(), envExpression.Substring(idx + 1, envExpression.Length - idx - 1));
    }

    public static ProcessExecutor<T, TE> ExecutionEnvironment<T, TE>(this ProcessExecutor<T, TE> executor, IDictionary<string, string> env) where T : IProcessResponseParser<TE>, IProcessResponse<TE>, new()
    {
      if (null == env || 0 == env.Count)
        return executor;

      foreach (var key in env.Keys)
      {
        executor.Env[key] = env[key];
      }

      return executor;
    }

    /// <summary>
    /// This function will extract the name value and verify that is is valid. It will then
    /// make sure that the value is wrapped inside double quotes if not yet wrapped.
    /// </summary>
    /// <param name="nameValue">The name=value strings</param>
    /// <returns>A list of name=value string with the value wrapped inside double quotes.</returns>
    public static IList<string> WrapValue(this TemplateString[] nameValue)
    {

      var list = new List<string>();
      foreach (var s in nameValue.Select(s => s.Rendered))
      {

        var index = s.IndexOf('=');
        if (-1 == index)
        {
          throw new FluentDockerException(
              $"Expected format name=value, missing equal sign in the name value string: '{s}'"
            );
        }

        var name = s.Substring(0, index);
        var value = s.Substring(index + 1, s.Length - index - 1).WrapWithChar("\"");

        list.Add($"{name}={value}");
      }

      return list;
    }
  }
}
