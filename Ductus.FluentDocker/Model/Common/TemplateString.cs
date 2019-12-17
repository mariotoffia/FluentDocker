using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ductus.FluentDocker.Common;

namespace Ductus.FluentDocker.Model.Common
{
  public sealed class TemplateString
  {
    private static readonly Dictionary<string, Func<string>> Templates;
    private static readonly Regex Urldetector = new Regex("((\"|')http(|s)://.*?(\"|'))", RegexOptions.Compiled);

    static TemplateString() => Templates =
        new Dictionary<string, Func<string>>
        {
          {
            "${TMP}", () =>
            {
              var path = DirectoryHelper.GetTempPath();
              if (path.StartsWith("/var/") && FdOs.IsOsx()) path = "/private/" + path;

              return path.Substring(0, path.Length - 1);
            }
          },
          {
            "${TEMP}", () =>
            {
              var path = DirectoryHelper.GetTempPath();
              if (path.StartsWith("/var/") && FdOs.IsOsx()) path = "/private/" + path;

              return path.Substring(0, path.Length - 1);
            }
          },
          {"${RND}", Path.GetRandomFileName},
          {"${PWD}", Directory.GetCurrentDirectory}
        };

    public TemplateString(string str, bool handleWindowsPathIfNeeded = false)
    {
      Original = str;
      Rendered = Render(ToTargetOs(str, handleWindowsPathIfNeeded));
    }

    public string Original { get; }
    public string Rendered { get; }

    private static string ToTargetOs(string str, bool handleWindowsPathIfNeeded)
    {
      if (string.IsNullOrEmpty(str) || str.StartsWith("emb:"))
        return str;

      if (!FdOs.IsWindows() || !handleWindowsPathIfNeeded)
      {
        return str;
      }

      var match = Urldetector.Match(str);
      if (!match.Success)
        return str.Replace('/', '\\');

      var res = "";
      var idx = 0;
      while (match.Success)
      {
        res += str.Substring(idx, match.Index - idx).Replace('/', '\\');
        res += str.Substring(match.Index, match.Length);
        idx = match.Index + match.Length;

        match = match.NextMatch();
      }

      res += str.Substring(idx).Replace('/', '\\');
      return res;
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
        if (-1 != str.IndexOf(tmpEnv, StringComparison.Ordinal))
          str = str.Replace(tmpEnv, (string)env.Value);
      }

      return str;
    }

    public static implicit operator TemplateString(string str) => null == str ? null : new TemplateString(str);

    public static implicit operator string(TemplateString str) => str?.Rendered;

    public override string ToString()
    {
      return Rendered;
    }
  }
}
