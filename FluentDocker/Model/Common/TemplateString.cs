using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentDocker.Common;

namespace FluentDocker.Model.Common
{
  /// <summary>
  /// Renders FluentDocker path templates such as ${TMP}, ${TEMP}, ${PWD}, ${RND}, and ${E_NAME}.
  /// Template tokens are always expanded when recognized; there is no escape syntax for a literal ${TMP}.
  /// </summary>
  public sealed partial class TemplateString(string str, bool handleWindowsPathIfNeeded = false)
  {
    private static readonly Dictionary<string, Func<string>> Templates;
    private static readonly Regex UrlDetector = MyRegex();

    static TemplateString() => Templates =
        new Dictionary<string, Func<string>>
        {
          {
            "${TMP}", () =>
            {
              var path = DirectoryHelper.GetTempPath();
              if (path.StartsWith("/var/", StringComparison.Ordinal) && FdOs.IsOsx()) path = "/private/" + path;

              return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
          },
          {
            "${TEMP}", () =>
            {
              var path = DirectoryHelper.GetTempPath();
              if (path.StartsWith("/var/", StringComparison.Ordinal) && FdOs.IsOsx()) path = "/private/" + path;

              return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
          },
          {"${RND}", Path.GetRandomFileName},
          {"${PWD}", Directory.GetCurrentDirectory}
        };

    /// <summary>Original template string supplied by the caller.</summary>
    public string Original { get; } = str;

    /// <summary>Rendered string after built-in and environment templates are expanded.</summary>
    public string Rendered { get; } = Render(ToTargetOs(str, handleWindowsPathIfNeeded));

    private static string ToTargetOs(string str, bool handleWindowsPathIfNeeded)
    {
      if (string.IsNullOrEmpty(str) || str.StartsWith("emb:", StringComparison.Ordinal))
        return str;

      if (!FdOs.IsWindows() || !handleWindowsPathIfNeeded)
      {
        return str;
      }

      var match = UrlDetector.Match(str);
      if (!match.Success)
        return str.Replace('/', '\\');

      var res = "";
      var idx = 0;
      while (match.Success)
      {
        res += str[idx..match.Index].Replace('/', '\\');
        res += str.Substring(match.Index, match.Length);
        idx = match.Index + match.Length;

        match = match.NextMatch();
      }

      res += str[idx..].Replace('/', '\\');
      return res;
    }

    private static string Render(string str)
    {
      str = Templates.Keys.Where(key => str.Contains(key, StringComparison.Ordinal))
        .Aggregate(str, (current, key) => ReplaceEach(current, key, Templates[key]));

      return RenderEnvironment(str);
    }

    private static string ReplaceEach(string str, string key, Func<string> valueFactory)
    {
      var index = str.IndexOf(key, StringComparison.Ordinal);
      while (index >= 0)
      {
        var value = valueFactory();
        str = str[..index] + value + str[(index + key.Length)..];
        index = str.IndexOf(key, index + value.Length, StringComparison.Ordinal);
      }

      return str;
    }

    private static string RenderEnvironment(string str)
    {
      foreach (DictionaryEntry env in Environment.GetEnvironmentVariables())
      {
        var tmpEnv = "${E_" + env.Key + "}";
        if (str.Contains(tmpEnv, StringComparison.Ordinal))
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

    [GeneratedRegex("((\"|')http(|s)://.*?(\"|'))", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
  }
}
