using System;
using System.Collections.Generic;

namespace FluentDocker.Testing.Core.Plugins
{
  /// <summary>
  /// Default implementation of <see cref="ITestPluginHost"/>.
  /// Manages plugin registration and resource factory resolution.
  /// </summary>
  public class TestPluginHost : ITestPluginHost, ITestPluginRegistry
  {
    private readonly Dictionary<string, Func<IServiceProvider, object>> _factories = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly HashSet<string> _registeredPlugins = new();

    /// <summary>
    /// Creates a plugin host with an optional service provider for dependency injection.
    /// </summary>
    /// <param name="serviceProvider">Service provider for factory dependency resolution.
    /// If null, a minimal provider that returns null for all types is used.</param>
    public TestPluginHost(IServiceProvider serviceProvider = null)
    {
      _serviceProvider = serviceProvider ?? NullServiceProvider.Instance;
    }

    /// <inheritdoc />
    public ITestPluginHost Add(ITestPlugin plugin)
    {
      if (plugin == null) throw new ArgumentNullException(nameof(plugin));

      if (_registeredPlugins.Contains(plugin.Id))
        return this; // Already registered, idempotent

      plugin.Register(this);
      _registeredPlugins.Add(plugin.Id);
      return this;
    }

    /// <inheritdoc />
    public TResource Create<TResource>() where TResource : class, IDockerResource
    {
      // Use the type name as the default key
      return Create<TResource>(typeof(TResource).Name);
    }

    /// <inheritdoc />
    public TResource Create<TResource>(string key) where TResource : class, IDockerResource
    {
      if (!_factories.TryGetValue(key, out var factory))
      {
        throw new InvalidOperationException(
            $"No factory registered for key '{key}'. " +
            "Ensure the plugin providing this resource has been added via Add().");
      }

      var result = factory(_serviceProvider);
      if (result is TResource resource)
        return resource;

      throw new InvalidCastException(
          $"Factory for key '{key}' produced {result?.GetType().Name ?? "null"}, " +
          $"expected {typeof(TResource).Name}.");
    }

    /// <inheritdoc />
    public bool HasFactory(string key)
    {
      return _factories.ContainsKey(key);
    }

    /// <inheritdoc />
    void ITestPluginRegistry.RegisterFactory<TResource>(
        string key, Func<IServiceProvider, TResource> factory)
    {
      if (string.IsNullOrEmpty(key))
        throw new ArgumentException("Factory key must not be null or empty.", nameof(key));
      if (factory == null)
        throw new ArgumentNullException(nameof(factory));

      _factories[key] = sp => factory(sp);
    }

    /// <summary>
    /// Minimal service provider that always returns null.
    /// </summary>
    private sealed class NullServiceProvider : IServiceProvider
    {
      public static readonly NullServiceProvider Instance = new();
      public object GetService(Type serviceType) => null;
    }
  }
}
