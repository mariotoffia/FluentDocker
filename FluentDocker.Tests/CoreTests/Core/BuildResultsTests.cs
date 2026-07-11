using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Kernel;
using FluentDocker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var scope = new BuildScope(kernel, "docker");
      var service1 = new MockService("service1");
      var service2 = new MockService("service2");
      scope.AddResult(service1);
      scope.AddResult(service2);

      var results = new BuildResults([scope]);

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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var scope1 = new BuildScope(kernel, "docker-1");
      var scope2 = new BuildScope(kernel, "docker-2");

      var service1 = new MockService("service1");
      var service2 = new MockService("service2");
      var service3 = new MockService("service3");

      scope1.AddResult(service1);
      scope1.AddResult(service2);
      scope2.AddResult(service3);

      var results = new BuildResults([scope1, scope2]);

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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var scope = new BuildScope(kernel, "docker");
      var service = new MockDisposableService();
      scope.AddResult(service);

      var results = new BuildResults([scope]);

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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var scope = new BuildScope(kernel, "docker");
      var service = new MockDisposableService();
      scope.AddResult(service);

      var results = new BuildResults([scope]);

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
      var results = new BuildResults([]);

      // Assert
      Assert.Empty(results.All);
      Assert.Empty(results.Scopes);
      Assert.Empty(results.ForDriver("any"));
    }

    [Fact]
    public void GetContainer_UsesCaseSensitiveNameMatching()
    {
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var scope = new BuildScope(kernel, "docker");
      var container = new Mock<IContainerService>();
      container.SetupGet(c => c.Name).Returns("CaseSensitive");
      scope.AddResult(container.Object);
      var results = new BuildResults([scope]);

      Assert.Same(container.Object, results.GetContainer("CaseSensitive"));
      Assert.Null(results.GetContainer("casesensitive"));

      kernel.Dispose();
    }

    // B-M3 pin: BuildResults disposes scopes in reverse of constructor-list order. Builder.BuildAsync
    // relies on this for cross-scope teardown (dependents before dependencies, e.g. containers before
    // the network they attach to) and now feeds it an explicit List<BuildScope> in creation order
    // (previously a Dictionary's .Values, an unspecified enumeration order) — this test guards the
    // BuildResults side of that contract so a future refactor cannot silently invert it.
    [Fact]
    public async Task DisposeAsync_DisposesScopesInReverseOfConstructorOrder()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var order = new List<string>();
      var scope1 = new BuildScope(kernel, "docker-1");
      var scope2 = new BuildScope(kernel, "docker-2");
      var scope3 = new BuildScope(kernel, "docker-3");
      scope1.AddResult(new OrderTrackingService("scope1", order));
      scope2.AddResult(new OrderTrackingService("scope2", order));
      scope3.AddResult(new OrderTrackingService("scope3", order));

      var results = new BuildResults([scope1, scope2, scope3]);

      // Act
      await results.DisposeAsync();

      // Assert: last scope created is the first disposed.
      Assert.Equal(["scope3", "scope2", "scope1"], order);

      // Cleanup
      kernel.Dispose();
    }

    // Mock services for testing
    private class MockService(string name = "test") : IServiceAsync
    {
      public string Name { get; } = name;
      public ServiceRunningState State => ServiceRunningState.Unknown;
      public FluentDockerKernel Kernel => null!;
      public string DriverId => "mock";

      public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
      public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string? uniqueName = null) => this;
      public IServiceAsync RemoveHook(string? uniqueName) => this;
#pragma warning disable CS0067, CS8618 // fake service: event never raised, never assigned
      public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CS0067, CS8618
      public virtual void Dispose() { }
      public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private class MockDisposableService : MockService
    {
      public bool WasDisposed { get; private set; }

#pragma warning disable CA2215 // Base Dispose/DisposeAsync are no-ops in this mock
      public override ValueTask DisposeAsync()
      {
        WasDisposed = true;
        return ValueTask.CompletedTask;
      }

      public override void Dispose()
      {
        WasDisposed = true;
      }
#pragma warning restore CA2215
    }

    // Records its own name into a shared list on disposal, so a multi-scope test can assert order.
    private sealed class OrderTrackingService(string name, List<string> order) : MockService(name)
    {
#pragma warning disable CA2215 // Base Dispose/DisposeAsync are no-ops in this mock
      public override ValueTask DisposeAsync()
      {
        order.Add(Name);
        return ValueTask.CompletedTask;
      }

      public override void Dispose()
      {
        order.Add(Name);
      }
#pragma warning restore CA2215
    }
  }
}
