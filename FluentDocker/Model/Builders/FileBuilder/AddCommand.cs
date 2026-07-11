#nullable enable
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>Represents a Dockerfile <c>ADD</c> instruction.</summary>
  /// <param name="source">Source path or URL.</param>
  /// <param name="destination">Destination path.</param>
  public sealed class AddCommand(TemplateString source, TemplateString destination) : ICommand
  {
    /// <summary>Gets the source path or URL.</summary>
    public TemplateString Source { get; internal set; } = source;
    /// <summary>Gets the destination path.</summary>
    public TemplateString Destination { get; } = destination;

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      var source = DockerfileJson.NormalizePath(Source.Rendered);
      var destination = DockerfileJson.NormalizePath(Destination.Rendered);
      return $"ADD {DockerfileJson.Array([source, destination])}";
    }
  }
}
