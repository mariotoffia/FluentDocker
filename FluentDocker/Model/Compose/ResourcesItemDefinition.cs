#nullable enable
namespace FluentDocker.Model.Compose
{
  /// <summary>A CPU/memory value pair used for both <see cref="ResourcesDefinition.Limits"/> and <see cref="ResourcesDefinition.Reservations"/>.</summary>
  public sealed class ResourcesItemDefinition
  {
    /// <summary>Fractional CPU count, e.g. <c>"0.50"</c> for half a core.</summary>
    public string? Cpus { get; set; }
    /// <summary>Memory amount, e.g. <c>"50M"</c>.</summary>
    public string? Memory { get; set; }
  }
}
