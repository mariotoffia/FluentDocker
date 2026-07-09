#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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

      return $"ARG {Name}={DefaultValue}";
    }
  }
}
