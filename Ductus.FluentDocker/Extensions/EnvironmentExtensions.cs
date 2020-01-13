using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Executors;

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

      if (idx == envExpression.Length -1)
        return new Tuple<string, string>(envExpression.Substring(0, idx).Trim(), string.Empty);

      return new Tuple<string, string>(envExpression.Substring(0, idx).Trim(), envExpression.Substring(idx + 1, envExpression.Length - idx - 1));
    }

    public static ProcessExecutor<T, TE> ExecutionEnvironment<T, TE>(this ProcessExecutor<T, TE> executor, IDictionary<string, string> env) where T : IProcessResponseParser<TE>, IProcessResponse<TE>, new()
    {
      if (null == env || 0 == env.Count)
        return executor;

      foreach(var key in env.Keys)
      {
        executor.Env[key] = env[key];
      }

      return executor;
    }
  }
}
