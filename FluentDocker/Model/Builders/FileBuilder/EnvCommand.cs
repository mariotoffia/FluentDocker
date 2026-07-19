#nullable enable
using System.Linq;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents one or more Dockerfile <c>ENV</c> assignments.</summary>
  public sealed class EnvCommand : ICommand
  {
    /// <summary>Creates environment assignments.</summary>
    /// <param name="nameValue">Environment assignments.</param>
    public EnvCommand(params TemplateString[] nameValue)
    {
      if (nameValue == null || 0 == nameValue.Length)
      {
        NameValue = [];
      }
      else
      {
        NameValue = [.. nameValue.WrapValue()];
      }
    }

    /// <summary>Gets rendered <c>name=value</c> assignments.</summary>
    public string[] NameValue { get; internal set; }

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      if (0 == NameValue.Length)
      {
        return "";
      }

      return $"ENV {string.Join(" ", NameValue)}";
    }
  }
}
