using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Core.Plugins;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class PluginHostTests
  {
    [Fact]
    public void Add_RegistersPlugin()
    {
      var host = new TestPluginHost();
      var plugin = new FakePlugin("test-plugin");

      host.Add(plugin);

      Assert.True(host.HasFactory("FakeResource"));
    }

    [Fact]
    public void Add_SamePluginTwice_IsIdempotent()
    {
      var host = new TestPluginHost();
      var plugin = new FakePlugin("test-plugin");

      host.Add(plugin);
      host.Add(plugin); // second add is a no-op

      Assert.True(host.HasFactory("FakeResource"));
    }

    [Fact]
    public void Add_NullPlugin_Throws()
    {
      var host = new TestPluginHost();
      Assert.Throws<ArgumentNullException>(() => host.Add(null));
    }

    [Fact]
    public void Create_RegisteredFactory_ReturnsResource()
    {
      var host = new TestPluginHost();
      host.Add(new FakePlugin("test-plugin"));

      var resource = host.Create<FakeResource>("FakeResource");

      Assert.NotNull(resource);
    }

    [Fact]
    public void Create_ByTypeName_ReturnsResource()
    {
      var host = new TestPluginHost();
      host.Add(new FakePlugin("test-plugin"));

      var resource = host.Create<FakeResource>();

      Assert.NotNull(resource);
    }

    [Fact]
    public void Create_UnregisteredKey_Throws()
    {
      var host = new TestPluginHost();

      Assert.Throws<InvalidOperationException>(
          () => host.Create<FakeResource>("nonexistent"));
    }

    [Fact]
    public void HasFactory_UnregisteredKey_ReturnsFalse()
    {
      var host = new TestPluginHost();
      Assert.False(host.HasFactory("nonexistent"));
    }

    [Fact]
    public void RegisterFactory_NullKey_Throws()
    {
      var host = new TestPluginHost();
      ITestPluginRegistry registry = host;

      Assert.Throws<ArgumentException>(
          () => registry.RegisterFactory<FakeResource>(null, _ => new FakeResource()));
    }

    [Fact]
    public void RegisterFactory_NullFactory_Throws()
    {
      var host = new TestPluginHost();
      ITestPluginRegistry registry = host;

      Assert.Throws<ArgumentNullException>(
          () => registry.RegisterFactory<FakeResource>("key", null));
    }

    [Fact]
    public void RegisterFactory_DuplicateKey_Throws()
    {
      var host = new TestPluginHost();
      // First plugin registers key "FakeResource"
      host.Add(new FakePlugin("plugin-a"));

      // Second plugin with different ID tries to register same key
      var ex = Assert.Throws<InvalidOperationException>(
          () => host.Add(new FakePlugin("plugin-b")));
      Assert.Contains("already registered", ex.Message);
    }

    [Fact]
    public void Add_NullPluginId_Throws()
    {
      var host = new TestPluginHost();
      Assert.Throws<ArgumentException>(
          () => host.Add(new FakePlugin(null)));
    }

    [Fact]
    public void Add_EmptyPluginId_Throws()
    {
      var host = new TestPluginHost();
      Assert.Throws<ArgumentException>(
          () => host.Add(new FakePlugin("")));
    }

    [Fact]
    public void Add_ReturnsSelf_ForFluent()
    {
      var host = new TestPluginHost();
      var result = host.Add(new FakePlugin("p1"));
      Assert.Same(host, result);
    }

    [Fact]
    public void Create_WithServiceProvider_PassesProvider()
    {
      var sp = new FakeServiceProvider();
      var host = new TestPluginHost(sp);
      host.Add(new ServiceProviderAwarePlugin());

      var resource = host.Create<FakeResource>("sp-aware");

      Assert.NotNull(resource);
    }

    #region Test Doubles

    private class FakeResource : ITestResource
    {
      public bool IsInitialized => false;
      public Task InitializeAsync(CancellationToken cancellationToken = default)
          => Task.CompletedTask;
      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private class FakePlugin : ITestPlugin
    {
      public string Id { get; }
      public FakePlugin(string id) => Id = id;

      public void Register(ITestPluginRegistry registry)
      {
        registry.RegisterFactory<FakeResource>("FakeResource", _ => new FakeResource());
      }
    }

    private class ServiceProviderAwarePlugin : ITestPlugin
    {
      public string Id => "sp-aware";

      public void Register(ITestPluginRegistry registry)
      {
        registry.RegisterFactory<FakeResource>("sp-aware", sp =>
        {
          // Verify that the service provider is passed through
          _ = sp.GetService(typeof(string)); // Just exercise it
          return new FakeResource();
        });
      }
    }

    private class FakeServiceProvider : IServiceProvider
    {
      public object GetService(Type serviceType) => null;
    }

    #endregion
  }
}
