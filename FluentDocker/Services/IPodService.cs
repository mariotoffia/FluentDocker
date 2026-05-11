namespace FluentDocker.Services
{
  /// <summary>
  /// Async pod service interface (Podman-specific).
  /// </summary>
  public interface IPodService : IServiceAsync
  {
    /// <summary>
    /// Pod identifier.
    /// </summary>
    string Id { get; }
  }
}
