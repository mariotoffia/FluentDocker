using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Common;

namespace Ductus.FluentDocker.Model.Common
{
  public sealed class TemplateString
  {
    private static readonly Dictionary<string, Func<string>> Templates;

    static TemplateString()
    {
      Templates =
        new Dictionary<string, Func<string>>
        {
          {
            "${TMP}", () =>
            {
              var path = Path.GetTempPath();
              if (path.StartsWith("/var/") && OperatingSystem.IsOsx()) path = "/private/" + path;
              return path.Substring(0, path.Length - 1);
            }
          },
          {
            "${TEMP}", () =>
            {
              var path = Path.GetTempPath();
              if (path.StartsWith("/var/") && OperatingSystem.IsOsx()) path = "/private/" + path;
              return path.Substring(0, path.Length - 1);
            }
          },
          {"${RND}", Path.GetRandomFileName},
          {"${PWD}", Directory.GetCurrentDirectory}
        };
    }

    public TemplateString(string str)
    {
      Original = str;
      Rendered = Render(ToTargetOs(str));
    }

    public string Original { get; }
    public string Rendered { get; }

    private static string ToTargetOs(string str)
    {
      if (string.IsNullOrEmpty(str) || str.StartsWith("emb:")) return str;

      return !OperatingSystem.IsWindows() ? str : str.Replace('/', '\\');
    }

    private static string Render(string str)
    {
      str = Templates.Keys.Where(key => -1 != str.IndexOf(key, StringComparison.Ordinal))
        .Aggregate(str, (current, key) => current.Replace(key, Templates[key]()));

      return RenderEnvironment(str);
    }

    private static string RenderEnvironment(string str)
    {
      foreach (DictionaryEntry env in Environment.GetEnvironmentVariables())
      {
        var tmpEnv = "${E_" + env.Key + "}";
        if (-1 != str.IndexOf(tmpEnv, StringComparison.Ordinal)) str = str.Replace(tmpEnv, (string) env.Value);
      }

      return str;
    }

    public static implicit operator TemplateString(string str)
    {
      return null == str ? null : new TemplateString(str);
    }

    public static implicit operator string(TemplateString str)
    {
      return str?.Rendered;
    }

    public override string ToString()
    {
      return Rendered;
    }
  }
}