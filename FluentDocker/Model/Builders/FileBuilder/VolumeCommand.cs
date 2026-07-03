using System.Linq;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class VolumeCommand : ICommand
  {
    public VolumeCommand(params TemplateString[] mountpoints)
      => Mountpoints = [.. (mountpoints ?? []).Select(s => s.Rendered)];

    public string[] Mountpoints { get; }

    public override string ToString()
    {
      return $"VOLUME {DockerfileJson.Array(Mountpoints)}";
    }
  }
}
