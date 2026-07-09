#nullable enable
using System;

namespace FluentDocker.Model.Builders.FileBuilder
{
  [Obsolete("Use LABEL maintainer=… instead")]
  public sealed class MaintainerCommand(string maintainer) : ICommand
  {
    public string Maintainer { get; } =
        DockerfileInstructionGuard.Require(maintainer, "MAINTAINER", "value", "MAINTAINER requires a value.");

    public override string ToString()
    {
      return $"MAINTAINER {Maintainer}";
    }
  }
}
