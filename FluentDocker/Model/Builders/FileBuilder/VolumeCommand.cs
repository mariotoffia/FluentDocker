#nullable enable
using System.Linq;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>VOLUME</c> instruction.</summary>
  public sealed class VolumeCommand : ICommand
  {
    /// <summary>Creates volume mount points.</summary>
    /// <param name="mountpoints">Mount points.</param>
    public VolumeCommand(params TemplateString[] mountpoints)
      => Mountpoints = [.. (mountpoints ?? []).Select(s => s.Rendered)];

    /// <summary>Gets the mount points.</summary>
    public string[] Mountpoints { get; }

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      return $"VOLUME {DockerfileJson.Array(Mountpoints)}";
    }
  }
}
