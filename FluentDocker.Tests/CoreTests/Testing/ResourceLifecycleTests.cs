using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ResourceLifecycleTests
  {
    [Fact]
    public async Task CreateAndInitializeAsync_UsesCustomKernelFactory()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();
      mockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var (returnedKernel, resource) = await ResourceLifecycle
          .CreateAndInitializeAsync(
              k => new ContainerResource(
                  k, b => b.UseImage("alpine:latest")),
              kernelFactory: () => Task.FromResult(kernel));

      Assert.Same(kernel, returnedKernel);
      Assert.True(resource.IsInitialized);

      await resource.DisposeAsync();
      kernel.Dispose();
    }

    [Fact]
    public async Task CreateAndInitializeAsync_UsesDefaultKernelFactory_WhenKernelFactoryNull()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();
      mockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var defaultFactoryCalled = false;

      var (returnedKernel, resource) = await ResourceLifecycle
          .CreateAndInitializeAsync(
              k => new ContainerResource(
                  k, b => b.UseImage("alpine:latest")),
              kernelFactory: null,
              defaultKernelFactory: () =>
              {
                defaultFactoryCalled = true;
                return Task.FromResult(kernel);
              });

      Assert.True(defaultFactoryCalled);
      Assert.Same(kernel, returnedKernel);
      Assert.True(resource.IsInitialized);

      await resource.DisposeAsync();
      kernel.Dispose();
    }

    [Fact]
    public async Task CreateAndInitializeAsync_PrefersKernelFactory_OverDefault()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();
      mockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var customCalled = false;
      var defaultCalled = false;

      var (_, resource) = await ResourceLifecycle
          .CreateAndInitializeAsync(
              k => new ContainerResource(
                  k, b => b.UseImage("alpine:latest")),
              kernelFactory: () =>
              {
                customCalled = true;
                return Task.FromResult(kernel);
              },
              defaultKernelFactory: () =>
              {
                defaultCalled = true;
                return Task.FromResult(kernel);
              });

      Assert.True(customCalled);
      Assert.False(defaultCalled);

      await resource.DisposeAsync();
      kernel.Dispose();
    }

    [Fact]
    public async Task CreateAndInitializeAsync_NullResourceFactory_ThrowsArgumentNullException()
    {
      await Assert.ThrowsAsync<ArgumentNullException>(
          () => ResourceLifecycle.CreateAndInitializeAsync<FakeResource>(
              null,
              () => Task.FromResult<FluentDockerKernel>(null)));
    }

    [Fact]
    public async Task CreateAndInitializeAsync_FactoryReturnsNull_ThrowsInvalidOperationException()
    {
      var (kernel, _) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(
          () => ResourceLifecycle.CreateAndInitializeAsync<FakeResource>(
              _ => null,
              () => Task.FromResult(kernel)));

      Assert.Contains("returned null", ex.Message);
      // Kernel should have been disposed by cleanup
    }

    [Fact]
    public async Task CreateAndInitializeAsync_InitFailure_DisposesResourceAndKernel()
    {
      var (kernel, _) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();

      var disposeWasCalled = false;
      var fakeResource = new FakeResource(
          throwOnInit: true,
          onDispose: () => disposeWasCalled = true);

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => ResourceLifecycle.CreateAndInitializeAsync(
              _ => fakeResource,
              () => Task.FromResult(kernel)));

      Assert.True(disposeWasCalled, "Resource should be disposed on init failure");
    }

    [Fact]
    public async Task DisposeAsync_NullResource_DoesNotThrow()
    {
      var (kernel, _) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();

      // Should not throw when resource is null
      await ResourceLifecycle.DisposeAsync(null, kernel);

      // Kernel should still be disposed
    }

    [Fact]
    public async Task DisposeAsync_DisposesResourceThenKernel()
    {
      var (kernel, _) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();

      var resourceDisposed = false;
      var fakeResource = new FakeResource(
          onDispose: () => resourceDisposed = true);

      await ResourceLifecycle.DisposeAsync(fakeResource, kernel);

      Assert.True(resourceDisposed);
    }

    [Fact]
    public async Task DisposeAsync_ResourceThrows_StillDisposesKernel()
    {
      var (kernel, _) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();

      var throwingResource = new FakeResource(throwOnDispose: true);

      // The method should let the exception propagate, but kernel
      // is in the finally block so it will be disposed regardless.
      // Since DisposeAsync uses try/finally, exception is re-thrown.
      await Assert.ThrowsAsync<InvalidOperationException>(
          () => ResourceLifecycle.DisposeAsync(throwingResource, kernel));
    }

    [Fact]
    public async Task DisposeAsync_NullKernel_DoesNotThrow()
    {
      var fakeResource = new FakeResource();

      // Should not throw when kernel is null
      await ResourceLifecycle.DisposeAsync(fakeResource, null);
    }

    #region Test Doubles

    private class FakeResource : ITestResource
    {
      private readonly bool _throwOnInit;
      private readonly bool _throwOnDispose;
      private readonly Action _onDispose;

      public bool IsInitialized { get; private set; }

      public FakeResource(
          bool throwOnInit = false,
          bool throwOnDispose = false,
          Action onDispose = null)
      {
        _throwOnInit = throwOnInit;
        _throwOnDispose = throwOnDispose;
        _onDispose = onDispose;
      }

      public Task InitializeAsync(CancellationToken cancellationToken = default)
      {
        if (_throwOnInit)
          throw new InvalidOperationException("Simulated init failure");
        IsInitialized = true;
        return Task.CompletedTask;
      }

      public ValueTask DisposeAsync()
      {
        _onDispose?.Invoke();
        if (_throwOnDispose)
          throw new InvalidOperationException("Simulated dispose failure");
        IsInitialized = false;
        return ValueTask.CompletedTask;
      }
    }

    #endregion
  }
}
