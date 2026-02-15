namespace FluentDocker.Testing.Core.Plugins
{
  /// <summary>
  /// Hosts plugins and provides access to registered resource factories.
  /// </summary>
  public interface ITestPluginHost
  {
    /// <summary>
    /// Adds a plugin to this host and invokes its registration.
    /// </summary>
    ITestPluginHost Add(ITestPlugin plugin);

    /// <summary>
    /// Creates a resource of the specified type using a registered factory.
    /// The factory is looked up by the type name if no explicit key was specified.
    /// </summary>
    TResource Create<TResource>() where TResource : class, ITestResource;

    /// <summary>
    /// Creates a resource using the factory registered under the specified key.
    /// </summary>
    TResource Create<TResource>(string key) where TResource : class, ITestResource;

    /// <summary>
    /// Returns whether a factory is registered for the specified key.
    /// </summary>
    bool HasFactory(string key);
  }
}
