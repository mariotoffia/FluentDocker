#nullable enable
using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>ARG</c> instruction.</summary>
  public sealed class ArgCommand : ICommand
  {
    /// <summary>Creates an argument instruction.</summary>
    /// <param name="name">Argument name.</param>
    /// <param name="defaultValue">Optional default value.</param>
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

    /// <summary>Gets the argument name.</summary>
    public string Name { get; }
    /// <summary>Gets the optional default value.</summary>
    public string? DefaultValue { get; }

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      if (string.IsNullOrEmpty(DefaultValue))
      {
        return $"ARG {Name}";
      }

      return $"ARG {Name}={QuoteDefaultValue(DefaultValue)}";
    }

    private static string QuoteDefaultValue(string value) =>
        new[] { new TemplateString($"ARG={value}") }.WrapValue()[0]["ARG=".Length..];
  }
}
