#nullable enable
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>RUN</c> instruction.</summary>
  /// <param name="run">Shell command to run.</param>
  public sealed class RunCommand(TemplateString run) : ICommand
  {
    /// <summary>Gets the shell command.</summary>
    public string Run { get; } = DockerfileInstructionGuard.Require(
        run, "RUN", "command", "RUN requires a command.");

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      return $"RUN {Run}";
    }
  }
}
