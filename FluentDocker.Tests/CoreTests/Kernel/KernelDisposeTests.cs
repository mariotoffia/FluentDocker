using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var mockPack = new AsyncDisposableMockDriverPack();
      var context = new DriverContext("docker");

      await kernel.RegisterDriverPackAsync("docker", mockPack, context, TestContext.Current.CancellationToken);

      // Act
      await kernel.DisposeAsync();

      // Assert
      Assert.True(mockPack.DisposeAsyncCalled);
    }

    [Fact]
    public async Task DisposeAsync_DisposesDisposableOnlyDriverPacks()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var mockPack = new DisposableMockDriverPack();
      var context = new DriverContext("docker");

      await kernel.RegisterDriverPackAsync("docker", mockPack, context, TestContext.Current.CancellationToken);

      // Act
      await kernel.DisposeAsync();

      // Assert
      Assert.True(mockPack.DisposeCalled);
    }

    [Fact]
    public async Task Dispose_DisposesDriverPacks_Synchronously()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var mockPack = new AsyncDisposableMockDriverPack();
      var context = new DriverContext("docker");

      await kernel.RegisterDriverPackAsync("docker", mockPack, context, TestContext.Current.CancellationToken);

      // Act
      kernel.Dispose();

      // Assert
      Assert.True(mockPack.DisposeAsyncCalled);
    }

    [Fact]
    public async Task DisposeAsync_DisposesMultipleDriverPacks()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var asyncPack = new AsyncDisposableMockDriverPack();
      var syncPack = new DisposableMockDriverPack();

      await kernel.RegisterDriverPackAsync("async-driver", asyncPack, new DriverContext("async-driver"), TestContext.Current.CancellationToken);
      await kernel.RegisterDriverPackAsync("sync-driver", syncPack, new DriverContext("sync-driver"), TestContext.Current.CancellationToken);

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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var mockPack = new AsyncDisposableMockDriverPack();
      var context = new DriverContext("docker");

      await kernel.RegisterDriverPackAsync("docker", mockPack, context, TestContext.Current.CancellationToken);

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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var throwingPack = new ThrowingDisposableMockDriverPack();
      var normalPack = new AsyncDisposableMockDriverPack();

      await kernel.RegisterDriverPackAsync("throwing", throwingPack, new DriverContext("throwing"), TestContext.Current.CancellationToken);
      await kernel.RegisterDriverPackAsync("normal", normalPack, new DriverContext("normal"), TestContext.Current.CancellationToken);

      // Act - should not throw even though one pack throws during disposal
      await kernel.DisposeAsync();

      // Assert - kernel is disposed (operations throw ObjectDisposedException)
      Assert.Throws<ObjectDisposedException>(() => kernel.IsDriverRegistered("any"));
    }

    [Fact]
    public async Task DisposeAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var mockPack = new AsyncDisposableMockDriverPack();
      var context = new DriverContext("docker");

      await kernel.RegisterDriverPackAsync("docker", mockPack, context, TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task BuildAsync_WhenLaterDriverRegistrationFails_DisposesAlreadyRegisteredPacks()
    {
      var initialized = new AsyncDisposableMockDriverPack();
      var failing = new ThrowingInitializeMockDriverPack();

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          new KernelBuilder(NullLoggerFactory.Instance)
              .WithDriver("ok", d => d.UseCustomDriverPack(initialized))
              .WithDriver("bad", d => d.UseCustomDriverPack(failing))
              .BuildAsync(TestContext.Current.CancellationToken));

      Assert.True(initialized.DisposeAsyncCalled);
    }

    [Fact]
    public async Task DisposeAsync_WhenRegistryTimeouts_RetriesOnce()
    {
      var registry = new TimeoutOnceRegistry();
      var kernel = new FluentDockerKernel(registry, NullLoggerFactory.Instance);

      await kernel.DisposeAsync();

      Assert.Equal(2, registry.DisposeCalls);
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

  internal class ThrowingInitializeMockDriverPack : MockDriverPack, IDriverPack
  {
    Task IDriverPack.InitializeAsync(DriverContext context, System.Threading.CancellationToken cancellationToken)
    {
      throw new InvalidOperationException("Simulated initialize error");
    }
  }

  internal sealed class TimeoutOnceRegistry : IDriverRegistry, IAsyncDisposable
  {
    public int DisposeCalls { get; private set; }

    public ValueTask DisposeAsync()
    {
      DisposeCalls++;
      if (DisposeCalls == 1)
        throw new TimeoutException("first dispose timed out");
      return ValueTask.CompletedTask;
    }

    public Task RegisterAsync(string driverId, IDriver driver, DriverContext context, System.Threading.CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public void Unregister(string driverId) => throw new NotSupportedException();
    public Task UnregisterAsync(string driverId, System.Threading.CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public IDriver GetDriver(string driverId) => throw new NotSupportedException();
    public bool TryGetDriver(string driverId, [NotNullWhen(true)] out IDriver? driver)
    {
      driver = null;
      return false;
    }

    public Task RegisterDriverPackAsync(string driverId, IDriverPack driverPack, DriverContext context, System.Threading.CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public IDriverPack GetDriverPack(string driverId) => throw new NotSupportedException();
    public bool TryGetDriverPack(string driverId, [NotNullWhen(true)] out IDriverPack? driverPack)
    {
      driverPack = null;
      return false;
    }

    public bool IsDriverPack(string driverId) => false;
    public DriverContext GetContext(string driverId) => throw new NotSupportedException();
    public bool IsRegistered(string driverId) => false;
    public IReadOnlyList<string> GetAllDriverIds() => [];
    public IReadOnlyList<string> GetDriversByType(DriverType driverType) => [];
    public IReadOnlyList<string> GetDriversByRuntime(RuntimeType runtime) => [];
    public string GetDefaultDriverId() => null!;
    public void SetDefaultDriver(string driverId) => throw new NotSupportedException();
  }

  #endregion
}
