using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ductus.FluentDocker.Model
{
  public sealed class TemplateString
  {
    private static readonly Dictionary<string, Func<string>> Templates;

    static TemplateString()
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

    public TemplateString(string str)
    {
      Original = str;
      Rendered = Render(str);
    }

    public string Original { get; }
    public string Rendered { get; }

    private static string Render(string str)
    {
      str = Templates.Keys.Where(key => -1 != str.IndexOf(key, StringComparison.Ordinal))
         .Aggregate(str, (current, key) => current.Replace(key, Templates[key]()));

      return RenderEnvionment(str);
    }

    private static string RenderEnvionment(string str)
    {
      foreach (DictionaryEntry env in Environment.GetEnvironmentVariables())
      {
        var tenv = "${E_" + env.Key + "}";
        if (-1 != str.IndexOf(tenv, StringComparison.Ordinal))
        {
          str = str.Replace(tenv, (string)env.Value);
        }
      }
      return str;
    }

    public static explicit operator TemplateString(string str)
    {
      return new TemplateString(str);
    }

    public static implicit operator string(TemplateString str)
    {
      return str.Rendered;
    }
  }
}
