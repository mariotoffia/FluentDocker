using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Kernel;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Core
{
  /// <summary>
  /// Tests for BuildScope class.
  /// </summary>
  [Trait("Category", "Unit")]
  public class BuildScopeTests
  {
    [Fact]
    public void Constructor_CreatesScope()
    {
      // Arrange
      var kernel = new FluentDockerKernel();

      // Act
      var scope = new BuildScope(kernel, "docker");

      // Assert
      Assert.Equal(kernel, scope.Kernel);
      Assert.Equal("docker", scope.DriverId);
      Assert.Empty(scope.Results);

      // Cleanup
      kernel.Dispose();
    }

    [Fact]
    public void AddResult_AddsService()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var scope = new BuildScope(kernel, "docker");
      var service = new MockService("test");

      // Act
      scope.AddResult(service);

      // Assert
      Assert.Single(scope.Results);
      Assert.Equal(service, scope.Results[0]);

      // Cleanup
      kernel.Dispose();
    }

    [Fact]
    public void AddResult_MultipleServices_AllAdded()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var scope = new BuildScope(kernel, "docker");
      var service1 = new MockService("service1");
      var service2 = new MockService("service2");
      var service3 = new MockService("service3");

      // Act
      scope.AddResult(service1);
      scope.AddResult(service2);
      scope.AddResult(service3);

      // Assert
      Assert.Equal(3, scope.Results.Count);
      Assert.Contains(service1, scope.Results);
      Assert.Contains(service2, scope.Results);
      Assert.Contains(service3, scope.Results);

      // Cleanup
      kernel.Dispose();
    }

    [Fact]
    public void AddResult_NullService_NotAdded()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var scope = new BuildScope(kernel, "docker");

      // Act
      scope.AddResult(null);

      // Assert
      Assert.Empty(scope.Results);

      // Cleanup
      kernel.Dispose();
    }

    [Fact]
    public void Results_IsReadOnly()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var scope = new BuildScope(kernel, "docker");
      var service = new MockService("test");
      scope.AddResult(service);

      // Act
      var results = scope.Results;

      // Assert
      Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<IServiceAsync>>(results);

      // Cleanup
      kernel.Dispose();
    }

    // Mock service for testing
    private class MockService : IServiceAsync
    {
      public MockService(string name) => Name = name;

      public string Name { get; }
      public ServiceRunningState State => ServiceRunningState.Unknown;
      public FluentDockerKernel Kernel => null;
      public string DriverId => "mock";

      public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
      public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string? uniqueName = null) => this;
      public IServiceAsync RemoveHook(string? uniqueName) => this;
#pragma warning disable CS0067
      public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CS0067
      public void Dispose() { }
      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
  }
}
