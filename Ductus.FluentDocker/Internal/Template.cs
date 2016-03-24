using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ductus.FluentDocker.Internal
{
  internal static class Template
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
          {"${RND}", Path.GetRandomFileName}
        };
    }

    internal static string Render(this string str)
    {
      return Templates.Keys.Where(key => -1 != str.IndexOf(key, StringComparison.Ordinal))
        .Aggregate(str, (current, key) => current.Replace(key, Templates[key]()));
    }

    // C:\Users\mario\AppData\Local\Temp
  }
}