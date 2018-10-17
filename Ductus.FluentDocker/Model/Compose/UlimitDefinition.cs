namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  /// Override the default ulimits for a container.
  /// </summary>
  /// <remarks>
  /// Specify same value to render a single line in the compose file.
  /// </remarks>
  public sealed class UlimitDefinition
  {
    public long MappingSoft { get; set; }
    public long MappingHard { get; set; }
  }
}