namespace FluentDocker.Services
{
  /// <summary>
  /// Allows callers to query which lifecycle operations a service supports,
  /// preventing runtime <see cref="System.NotSupportedException"/> from unsupported operations.
  /// </summary>
  public interface IServiceCapabilities
  {
    bool CanStart { get; }
    bool CanStop { get; }
    bool CanPause { get; }
    bool CanRemove { get; }
  }
}
