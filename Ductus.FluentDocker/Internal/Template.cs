using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ductus.FluentDocker.Internal
{
  public static class Template
  {
    private static readonly Dictionary<string, Func<string>> Templates;

    static Template()
    {
      Templates =
        new Dictionary<string, Func<string>>
        {
          {"${TEMP}", () =>
          {
            var path = Path.GetTempPath();
            return path.Substring(0, path.Length - 1);
          }},
          {"${RND}", Path.GetRandomFileName},
          {"${PWD}", Directory.GetCurrentDirectory }
        };
    }

    public static string Render(this string str)
    {
     str = Templates.Keys.Where(key => -1 != str.IndexOf(key, StringComparison.Ordinal))
        .Aggregate(str, (current, key) => current.Replace(key, Templates[key]()));

      return RenderEnvionment(str);
    }

    public static string RenderEnvionment(this string str)
    {
      foreach(DictionaryEntry env in Environment.GetEnvironmentVariables())
      {
        var tenv = "${E_" + env.Key + "}";
        if (-1 != str.IndexOf(tenv, StringComparison.Ordinal))
        {
          str = str.Replace(tenv, (string)env.Value);
        }
      }
      return str;
    }
  }
}