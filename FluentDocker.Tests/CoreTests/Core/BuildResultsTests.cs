using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Kernel;
using FluentDocker.Services;
using Xunit;

#pragma warning disable CS0618 // IService obsolete — intentional test usage

namespace FluentDocker.Tests.CoreTests.Core
{
  /// <summary>
  /// Tests for BuildResults class.
  /// </summary>
  [Trait("Category", "Unit")]
  public class BuildResultsTests
  {
    [Fact]
    public void Constructor_CreatesResults()
    {
      // Arrange
      var scopes = new List<BuildScope>();

      // Act
      var results = new BuildResults(scopes);

      // Assert
      Assert.NotNull(results);
      Assert.NotNull(results.All);
      Assert.Empty(results.All);
    }

    [Fact]
    public void All_ReturnsAllServices()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var scope = new BuildScope(kernel, "docker");
      var service1 = new MockService("service1");
      var service2 = new MockService("service2");
      scope.AddResult(service1);
      scope.AddResult(service2);

      var results = new BuildResults(new List<BuildScope> { scope });

      // Act
      var all = results.All;

      // Assert
      Assert.Equal(2, all.Count);
      Assert.Contains(service1, all);
      Assert.Contains(service2, all);

      // Cleanup
      kernel.Dispose();
    }

    [Fact]
    public void ForDriver_FiltersCorrectly()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var scope1 = new BuildScope(kernel, "docker-1");
      var scope2 = new BuildScope(kernel, "docker-2");

      var service1 = new MockService("service1");
      var service2 = new MockService("service2");
      var service3 = new MockService("service3");

      scope1.AddResult(service1);
      scope1.AddResult(service2);
      scope2.AddResult(service3);

      var results = new BuildResults(new List<BuildScope> { scope1, scope2 });

      // Act
      var driver1Services = results.ForDriver("docker-1");
      var driver2Services = results.ForDriver("docker-2");

      // Assert
      Assert.Equal(2, driver1Services.Count);
      Assert.Contains(service1, driver1Services);
      Assert.Contains(service2, driver1Services);
      Assert.Single(driver2Services);
      Assert.Contains(service3, driver2Services);

      // Cleanup
      kernel.Dispose();
    }

    [Fact]
    public void Scopes_ReturnsAllScopes()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var scope1 = new BuildScope(kernel, "docker-1");
      var scope2 = new BuildScope(kernel, "docker-2");
      var scopes = new List<BuildScope> { scope1, scope2 };

      // Act
      var results = new BuildResults(scopes);

      // Assert
      Assert.Equal(2, results.Scopes.Count);
      Assert.Contains(scope1, results.Scopes);
      Assert.Contains(scope2, results.Scopes);

      // Cleanup
      kernel.Dispose();
    }

    [Fact]
    public async Task DisposeAllAsync_DisposesServices()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var scope = new BuildScope(kernel, "docker");
      var service = new MockDisposableService();
      scope.AddResult(service);

      var results = new BuildResults(new List<BuildScope> { scope });

      // Act
      await results.DisposeAllAsync();

      // Assert
      Assert.True(service.WasDisposed);

      // Cleanup
      kernel.Dispose();
    }

    [Fact]
    public void Dispose_DisposesServices()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var scope = new BuildScope(kernel, "docker");
      var service = new MockDisposableService();
      scope.AddResult(service);

      var results = new BuildResults(new List<BuildScope> { scope });

      // Act
      results.Dispose();

      // Assert
      Assert.True(service.WasDisposed);

      // Cleanup
      kernel.Dispose();
    }

    [Fact]
    public void EmptyResults_HandledCorrectly()
    {
      // Act
      var results = new BuildResults(new List<BuildScope>());

      // Assert
      Assert.Empty(results.All);
      Assert.Empty(results.Scopes);
      Assert.Empty(results.ForDriver("any"));
    }

    // Mock services for testing
    private class MockService : IService
    {
      public MockService(string name = "test") => Name = name;

      public string Name { get; }
      public ServiceRunningState State => ServiceRunningState.Unknown;

      public void Start() { }
      public void Pause() { }
      public void Stop() { }
      public void Remove(bool force = false) { }
      public IService AddHook(ServiceRunningState state, System.Action<IService> hook, string? uniqueName = null) => this;
      public IService RemoveHook(string uniqueName) => this;
#pragma warning disable CS0067
      public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CS0067
      public void Dispose() { }
    }

    private class MockDisposableService : MockService, System.IAsyncDisposable
    {
      public bool WasDisposed { get; private set; }

      public ValueTask DisposeAsync()
      {
        WasDisposed = true;
        return ValueTask.CompletedTask;
      }

      public new void Dispose()
      {
        WasDisposed = true;
      }
    }
  }
}
