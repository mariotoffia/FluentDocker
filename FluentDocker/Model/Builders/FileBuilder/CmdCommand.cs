#nullable enable
using System;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>CMD</c> instruction in exec form.</summary>
  /// <param name="cmd">Executable or command.</param>
  /// <param name="args">Command arguments.</param>
  public sealed class CmdCommand(string cmd, params string[] args) : ICommand
  {
    /// <summary>Gets the executable or command.</summary>
    public string Cmd { get; } = string.IsNullOrEmpty(cmd)
        ? throw new ArgumentException("CMD requires a command.", nameof(cmd))
        : cmd;
    /// <summary>Gets the command arguments.</summary>
    public string[] Arguments { get; } = args ?? [];

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      return $"CMD {DockerfileJson.Array([Cmd, .. Arguments])}";
    }
  }
}
