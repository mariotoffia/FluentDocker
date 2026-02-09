using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  /// <summary>
  /// Unit tests for FluentDockerKernel disposal behavior (IDisposable / IAsyncDisposable).
  /// </summary>
  [Trait("Category", "Unit")]
  public class FluentDockerKernelDisposeTests
  {
    [Fact]
    public async Task DisposeAsync_DisposesAsyncDisposableDriverPacks()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var mockPack = new AsyncDisposableMockDriverPack();
      var context = new DriverContext("docker");

      await kernel.RegisterDriverPackAsync("docker", mockPack, context);

      // Act
      await kernel.DisposeAsync();

      // Assert
      Assert.True(mockPack.DisposeAsyncCalled);
    }

    [Fact]
    public async Task DisposeAsync_DisposesDisposableOnlyDriverPacks()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var mockPack = new DisposableMockDriverPack();
      var context = new DriverContext("docker");

      await kernel.RegisterDriverPackAsync("docker", mockPack, context);

      // Act
      await kernel.DisposeAsync();

      // Assert
      Assert.True(mockPack.DisposeCalled);
    }

    [Fact]
    public void Dispose_DisposesDriverPacks_Synchronously()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var mockPack = new AsyncDisposableMockDriverPack();
      var context = new DriverContext("docker");

      kernel.RegisterDriverPackAsync("docker", mockPack, context)
          .GetAwaiter().GetResult();

      // Act
      kernel.Dispose();

      // Assert
      Assert.True(mockPack.DisposeAsyncCalled);
    }

    [Fact]
    public async Task DisposeAsync_DisposesMultipleDriverPacks()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var asyncPack = new AsyncDisposableMockDriverPack();
      var syncPack = new DisposableMockDriverPack();

      await kernel.RegisterDriverPackAsync("async-driver", asyncPack, new DriverContext("async-driver"));
      await kernel.RegisterDriverPackAsync("sync-driver", syncPack, new DriverContext("sync-driver"));

      // Act
      await kernel.DisposeAsync();

      // Assert
      Assert.True(asyncPack.DisposeAsyncCalled);
      Assert.True(syncPack.DisposeCalled);
    }

    [Fact]
    public async Task DisposeAsync_MultipleCallsSafe()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var mockPack = new AsyncDisposableMockDriverPack();
      var context = new DriverContext("docker");

      await kernel.RegisterDriverPackAsync("docker", mockPack, context);

      // Act - dispose twice
      await kernel.DisposeAsync();
      await kernel.DisposeAsync();

      // Assert - only disposed once
      Assert.Equal(1, mockPack.DisposeAsyncCallCount);
    }

    [Fact]
    public async Task DisposeAsync_ContinuesOnDisposalError()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var throwingPack = new ThrowingDisposableMockDriverPack();
      var normalPack = new AsyncDisposableMockDriverPack();

      await kernel.RegisterDriverPackAsync("throwing", throwingPack, new DriverContext("throwing"));
      await kernel.RegisterDriverPackAsync("normal", normalPack, new DriverContext("normal"));

      // Act - should not throw even though one pack throws during disposal
      await kernel.DisposeAsync();

      // Assert - kernel is disposed (operations throw ObjectDisposedException)
      Assert.Throws<ObjectDisposedException>(() => kernel.IsDriverRegistered("any"));
    }

    [Fact]
    public async Task DisposeAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var mockPack = new AsyncDisposableMockDriverPack();
      var context = new DriverContext("docker");

      await kernel.RegisterDriverPackAsync("docker", mockPack, context);

      // Act
      await kernel.DisposeAsync();

      // Assert - operations throw after disposal
      Assert.Throws<ObjectDisposedException>(() =>
          kernel.SysCtl<IContainerDriver>("docker"));
    }

    [Fact]
    public async Task DisposeAsync_NonDisposableDriverPack_JustUnregisters()
    {
      // Arrange - MockDriverPack does not implement IDisposable or IAsyncDisposable
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      // Act - should not throw
      await kernel.DisposeAsync();

      // Assert - kernel is disposed
      Assert.Throws<ObjectDisposedException>(() => kernel.IsDriverRegistered("docker"));
    }
  }

  #region Test Helpers for Disposal

  /// <summary>
  /// Mock driver pack that implements IAsyncDisposable for disposal tests.
  /// </summary>
  internal class AsyncDisposableMockDriverPack : MockDriverPack, IAsyncDisposable
  {
    public bool DisposeAsyncCalled { get; private set; }
    public int DisposeAsyncCallCount { get; private set; }

    public ValueTask DisposeAsync()
    {
      DisposeAsyncCalled = true;
      DisposeAsyncCallCount++;
      return ValueTask.CompletedTask;
    }
  }

  /// <summary>
  /// Mock driver pack that implements IDisposable (but not IAsyncDisposable) for disposal tests.
  /// </summary>
  internal class DisposableMockDriverPack : MockDriverPack, IDisposable
  {
    public bool DisposeCalled { get; private set; }
    public int DisposeCallCount { get; private set; }

    public void Dispose()
    {
      DisposeCalled = true;
      DisposeCallCount++;
    }
  }

  /// <summary>
  /// Mock driver pack that throws during disposal to test error resilience.
  /// </summary>
  internal class ThrowingDisposableMockDriverPack : MockDriverPack, IAsyncDisposable
  {
    public ValueTask DisposeAsync()
    {
      throw new InvalidOperationException("Simulated disposal error");
    }
  }

  #endregion
}
