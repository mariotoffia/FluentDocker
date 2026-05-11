using System.Collections.Generic;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Model.Common;

namespace FluentDocker.Extensions
{
  public static class EnvironmentExtensions
  {
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

        var name = s[..index];
        var value = s[(index + 1)..].WrapWithChar("\"");

        list.Add($"{name}={value}");
      }

      return list;
    }
  }
}
