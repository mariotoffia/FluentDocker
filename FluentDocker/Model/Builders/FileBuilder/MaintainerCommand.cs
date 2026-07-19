#nullable enable
using System;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents the legacy Dockerfile <c>MAINTAINER</c> instruction.</summary>
  /// <param name="maintainer">Maintainer value.</param>
  [Obsolete("Use LABEL maintainer=… instead")]
  public sealed class MaintainerCommand(string maintainer) : ICommand
  {
    /// <summary>Gets the maintainer value.</summary>
    public string Maintainer { get; } =
        DockerfileInstructionGuard.Require(maintainer, "MAINTAINER", "value", "MAINTAINER requires a value.");

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      return $"MAINTAINER {Maintainer}";
    }
  }
}
