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

    // Non-null during Add() to stage factories atomically.
    private Dictionary<string, Func<IServiceProvider, object>> _staging;

    /// <summary>
    /// Creates a plugin host with an optional service provider for dependency injection.
    /// </summary>
    /// <param name="serviceProvider">Service provider for factory dependency resolution.
    /// If null, a minimal provider that returns null for all types is used.</param>
    public TestPluginHost(IServiceProvider serviceProvider = null) => _serviceProvider = serviceProvider ?? NullServiceProvider.Instance;

    /// <inheritdoc />
    public ITestPluginHost Add(ITestPlugin plugin)
    {
      if (plugin == null)
        throw new ArgumentNullException(nameof(plugin));

      if (string.IsNullOrEmpty(plugin.Id))
        throw new ArgumentException(
            "Plugin.Id must not be null or empty.", nameof(plugin));

      if (_registeredPlugins.Contains(plugin.Id))
        return this; // Already registered, idempotent

      _staging = new Dictionary<string, Func<IServiceProvider, object>>();
      try
      {
        plugin.Register(this);

        // Validate no collisions with committed factories
        foreach (var key in _staging.Keys)
        {
          if (_factories.ContainsKey(key))
            throw new InvalidOperationException(
                $"A factory for key '{key}' is already registered. " +
                "Plugin key collisions must be resolved by using unique keys.");
        }

        // Commit all staged factories atomically
        foreach (var kvp in _staging)
          _factories[kvp.Key] = kvp.Value;

        _registeredPlugins.Add(plugin.Id);
      }
      finally
      {
        _staging = null;
      }

      return this;
    }

    /// <inheritdoc />
    public TResource Create<TResource>() where TResource : class, ITestResource
    {
      // Use the type name as the default key
      return Create<TResource>(typeof(TResource).Name);
    }

    /// <inheritdoc />
    public TResource Create<TResource>(string key) where TResource : class, ITestResource
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

      var target = _staging ?? _factories;

      if (target.ContainsKey(key))
        throw new InvalidOperationException(
            $"A factory for key '{key}' is already registered. " +
            "Plugin key collisions must be resolved by using unique keys.");

      // When staging, also check committed factories for duplicates
      if (_staging != null && _factories.ContainsKey(key))
        throw new InvalidOperationException(
            $"A factory for key '{key}' is already registered. " +
            "Plugin key collisions must be resolved by using unique keys.");

      target[key] = sp => factory(sp);
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
