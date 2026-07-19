#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>USER</c> instruction.</summary>
  public sealed class UserCommand : ICommand
  {
    /// <summary>Creates a user instruction.</summary>
    /// <param name="user">User name or id.</param>
    /// <param name="group">Optional group name or id.</param>
    public UserCommand(TemplateString user, TemplateString? group = null)
    {
      if (null == user || string.IsNullOrEmpty(user.Rendered))
      {
        throw new FluentDockerException("Must specify username or user id");
      }


      User = DockerfileInstructionGuard.Validate(user.Rendered, "USER", "user");

      if (null != group && !string.IsNullOrEmpty(group.Rendered))
      {
        Group = DockerfileInstructionGuard.Validate(group.Rendered, "USER", "group");
      }
    }

    /// <summary>Gets the user name or id.</summary>
    public string User { get; }
    /// <summary>Gets the optional group name or id.</summary>
    public string? Group { get; }

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      if (string.IsNullOrEmpty(Group))
      {
        return $"USER {User}";
      }

      return $"USER {User}:{Group}";
    }
  }
}
