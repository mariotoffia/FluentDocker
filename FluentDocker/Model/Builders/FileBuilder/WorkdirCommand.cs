#nullable enable
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>WORKDIR</c> instruction.</summary>
  /// <param name="workdir">Working directory path.</param>
  public sealed class WorkdirCommand(string workdir) : ICommand
  {
    /// <summary>Gets the working directory path.</summary>
    public string Workdir { get; } = DockerfileInstructionGuard.Require(
        workdir, "WORKDIR", "path", "WORKDIR requires a path.");

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      return $"WORKDIR {Workdir}";
    }
  }
}
