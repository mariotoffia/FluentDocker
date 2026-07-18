#nullable enable
using System;
using System.Linq;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>VOLUME</c> instruction.</summary>
  public sealed class VolumeCommand : ICommand
  {
    /// <summary>Creates volume mount points.</summary>
    /// <param name="mountpoints">Mount points.</param>
    /// <exception cref="ArgumentException"><paramref name="mountpoints"/> is null or empty (<c>VOLUME []</c> is not a valid instruction).</exception>
    public VolumeCommand(params TemplateString[] mountpoints)
    {
      if (mountpoints is null || mountpoints.Length == 0)
        throw new ArgumentException(
            "VOLUME requires at least one mount point; an empty list would render an invalid Dockerfile instruction.",
            nameof(mountpoints));
      Mountpoints = [.. mountpoints.Select(s => s.Rendered)];
    }

    /// <summary>Gets the mount points.</summary>
    public string[] Mountpoints { get; }

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      return $"VOLUME {DockerfileJson.Array(Mountpoints)}";
    }
  }
}
