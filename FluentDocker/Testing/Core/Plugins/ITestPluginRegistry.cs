using System;

namespace FluentDocker.Testing.Core.Plugins
{
  /// <summary>
  /// Registry where plugins register their resource factories.
  /// </summary>
  public interface ITestPluginRegistry
  {
    /// <summary>
    /// Registers a factory that creates a resource of type <typeparamref name="TResource"/>.
    /// </summary>
    /// <typeparam name="TResource">The resource type (must implement <see cref="ITestResource"/>).</typeparam>
    /// <param name="key">Unique key for this factory (e.g., "postgres", "rabbitmq").</param>
    /// <param name="factory">Factory function. Receives an <see cref="IServiceProvider"/>
    /// for resolving dependencies like the kernel.</param>
    void RegisterFactory<TResource>(string key, Func<IServiceProvider, TResource> factory)
        where TResource : class, ITestResource;
  }
}
