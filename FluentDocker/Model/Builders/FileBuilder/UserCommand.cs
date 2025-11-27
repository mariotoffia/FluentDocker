using System;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class UserCommand : ICommand
  {
    public UserCommand(TemplateString user, TemplateString group = null)
    {
      if (null == user || string.IsNullOrEmpty(user.Rendered))
      {
        throw new FluentDockerException("Must specify username or user id");
      }


      User = user.Rendered;

      if (null != group && !string.IsNullOrEmpty(group.Rendered))
      {
        Group = group.Rendered;
      }
    }

    public string User { get; }
    public string Group { get; }

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
