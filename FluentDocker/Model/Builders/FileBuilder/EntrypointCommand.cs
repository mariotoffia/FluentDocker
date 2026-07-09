#nullable enable
using System;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>ENTRYPOINT</c> instruction in exec form.</summary>
  /// <param name="executable">Executable to run.</param>
  /// <param name="args">Executable arguments.</param>
  public sealed class EntrypointCommand(string executable, params string[] args) : ICommand
  {
    /// <summary>Gets the executable.</summary>
    public string Executable { get; } = string.IsNullOrEmpty(executable)
        ? throw new ArgumentException("ENTRYPOINT requires a command.", nameof(executable))
        : executable;
    /// <summary>Gets the executable arguments.</summary>
    public string[] Arguments { get; } = args ?? [];

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      return $"ENTRYPOINT {DockerfileJson.Array([Executable, .. Arguments])}";
    }
  }
}
