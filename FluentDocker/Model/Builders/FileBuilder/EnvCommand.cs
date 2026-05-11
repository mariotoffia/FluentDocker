using System.Linq;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class EnvCommand : ICommand
  {
    public EnvCommand(params TemplateString[] nameValue)
    {
      if (nameValue == null || 0 == nameValue.Length)
      {
        NameValue = [];
      }
      else
      {
        NameValue = NameValue = [.. nameValue.WrapValue()];
      }
    }

    public string[] NameValue { get; internal set; }

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
