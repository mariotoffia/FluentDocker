#nullable enable
namespace FluentDocker.Model.Compose
{
  /// <summary>
  /// Override the default ulimits for a container.
  /// </summary>
  /// <remarks>
  /// Specify same value to render a single line in the compose file.
  /// </remarks>
  public sealed class UlimitDefinition
  {
    /// <summary>The soft limit; when equal to <see cref="MappingHard"/> the ulimit renders as a single value instead of a soft/hard mapping.</summary>
    public long MappingSoft { get; set; }
    /// <summary>The hard limit; when equal to <see cref="MappingSoft"/> the ulimit renders as a single value instead of a soft/hard mapping.</summary>
    public long MappingHard { get; set; }
  }
}
