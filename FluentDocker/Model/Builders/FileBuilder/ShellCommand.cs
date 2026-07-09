#nullable enable
using System;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>SHELL</c> instruction.</summary>
  /// <param name="shell">Shell executable.</param>
  /// <param name="args">Shell arguments.</param>
  public sealed class ShellCommand(string shell, params string[] args) : ICommand
  {
    /// <summary>Gets the shell executable.</summary>
    public string Shell { get; } = string.IsNullOrEmpty(shell)
        ? throw new ArgumentException("SHELL requires a command.", nameof(shell))
        : shell;
    /// <summary>Gets the shell arguments.</summary>
    public string[] Arguments { get; } = args ?? [];

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      return $"SHELL {DockerfileJson.Array([Shell, .. Arguments])}";
    }
  }
}
