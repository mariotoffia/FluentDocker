using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using System.Linq;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class EnvCommand : ICommand
  {
    public EnvCommand(params TemplateString[] nameValue)
    {
        if (nameValue == null || 0 == nameValue.Length) {
            NameValue = new string[0];
        } else {
            NameValue = NameValue = nameValue.WrapValue().ToArray();
        }
    }

    public string[] NameValue { get; internal set; }

    public override string ToString()
    {
      if (0 == NameValue.Length) {
          return "";
      }

      return $"ENV {string.Join(" ", NameValue)}";
    }
  }
}
