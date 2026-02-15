namespace FluentDocker.Testing.Core.Plugins
{
  /// <summary>
  /// Entry point for external test plugin assemblies.
  /// A plugin registers its resource factories with the <see cref="ITestPluginRegistry"/>.
  /// </summary>
  public interface ITestPlugin
  {
    /// <summary>
    /// Unique identifier for this plugin (e.g., "FluentDocker.Testing.Plugin.Postgres").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Registers resource factories and other extension points with the registry.
    /// </summary>
    void Register(ITestPluginRegistry registry);
  }
}
