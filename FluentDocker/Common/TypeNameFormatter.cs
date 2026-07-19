using System;
using System.Linq;

namespace FluentDocker.Common
{
  internal static class TypeNameFormatter
  {
    public static string Format(Type type)
    {
      if (!type.IsGenericType)
        return type.Name;

      var name = type.Name;
      var tick = name.IndexOf('`');
      if (tick >= 0)
        name = name[..tick];

      return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Format))}>";
    }
  }
}
