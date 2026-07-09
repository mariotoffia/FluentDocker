#nullable enable
using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class ArgCommand : ICommand
  {
    public ArgCommand(TemplateString name, TemplateString? defaultValue = null)
    {
      if (null == name || string.IsNullOrEmpty(name.Rendered))
      {
        throw new FluentDockerException("Must, at least, specify the argument name in a ARG");
      }


      Name = DockerfileInstructionGuard.Validate(name.Rendered, "ARG", "name");

      if (null != defaultValue && !string.IsNullOrEmpty(defaultValue.Rendered))
      {
        DefaultValue = DockerfileInstructionGuard.Validate(defaultValue.Rendered, "ARG", "default value");
      }
    }

    public string Name { get; }
    public string? DefaultValue { get; }

    public override string ToString()
    {
      if (string.IsNullOrEmpty(DefaultValue))
      {
        return $"ARG {Name}";
      }

      return $"ARG {Name}={QuoteDefaultValue(DefaultValue)}";
    }

    private static string QuoteDefaultValue(string value) =>
        new[] { (TemplateString)$"ARG={value}" }.WrapValue()[0]["ARG=".Length..];
  }
}
