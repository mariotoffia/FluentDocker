using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  /// <summary>
  /// Tests for TrySysCtl and SysCtl(driverId, Type) on FluentDockerKernel.
  /// </summary>
  [Trait("Category", "Unit")]
  public class TrySysCtlTests
  {
    private static async Task<(FluentDockerKernel kernel, MockDriverPack pack)> CreateKernelWithMockPack()
    {
      var pack = new MockDriverPack();
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync("test", pack, new DriverContext("test"));
      return (kernel, pack);
    }

    [Fact]
    public async Task TrySysCtl_ExistingInterface_ReturnsTrueAndInstance()
    {
      // Arrange
      var (kernel, _) = await CreateKernelWithMockPack();

      // Act
      var result = kernel.TrySysCtl<IContainerDriver>("test", out var instance);

      // Assert
      Assert.True(result);
      Assert.NotNull(instance);
    }

    [Fact]
    public async Task TrySysCtl_NonExistingInterface_ReturnsFalseAndNull()
    {
      // Arrange
      var (kernel, _) = await CreateKernelWithMockPack();

      // Act
      var result = kernel.TrySysCtl<IDisposable>("test", out var instance);

      // Assert
      Assert.False(result);
      Assert.Null(instance);
    }

    [Fact]
    public async Task TrySysCtl_WhenPackTryResolveThrowsInterfaceNotSupported_ReturnsFalse()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "test",
          new ThrowingUnsupportedPack(),
          new DriverContext("test"),
          TestContext.Current.CancellationToken);

      var result = kernel.TrySysCtl<IDisposable>("test", out var instance);

      Assert.False(result);
      Assert.Null(instance);
    }

    [Fact]
    public async Task TrySysCtl_WhenDriverResolverThrowsInterfaceNotSupported_ReturnsFalse()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverAsync(
          "test",
          new ThrowingResolverDriver(),
          new DriverContext("test"),
          TestContext.Current.CancellationToken);

      var result = kernel.TrySysCtl<IDisposable>("test", out var instance);

      Assert.False(result);
      Assert.Null(instance);
    }

    [Fact]
    public async Task TrySysCtl_AfterUnregister_CannotResolveFreshPortAndDisposesPack()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var pack = new DisposableResolvingPack();
      await kernel.RegisterDriverPackAsync(
          "test", pack, new DriverContext("test"), TestContext.Current.CancellationToken);
      var resolved = kernel.TrySysCtl<IContainerDriver>("test", out var port);
      Assert.True(resolved);
      Assert.NotNull(port);

      await kernel.UnregisterDriverAsync("test", TestContext.Current.CancellationToken);

      Assert.True(pack.Disposed);
      Assert.Throws<DriverNotFoundException>(() => kernel.SysCtl<IContainerDriver>("test"));
    }

    [Fact]
    public async Task TrySysCtl_NonExistingDriver_ThrowsDriverNotFound()
    {
      // Arrange
      var (kernel, _) = await CreateKernelWithMockPack();

      // Act & Assert
      Assert.Throws<DriverNotFoundException>(() =>
          kernel.TrySysCtl<IContainerDriver>("non-existent", out _));
    }

    [Fact]
    public async Task TrySysCtl_AfterDispose_ThrowsObjectDisposed()
    {
      // Arrange
      var (kernel, _) = await CreateKernelWithMockPack();
      kernel.Dispose();

      // Act & Assert
      Assert.Throws<ObjectDisposedException>(() =>
          kernel.TrySysCtl<IContainerDriver>("test", out _));
    }

    [Fact]
    public async Task SysCtlByType_ExistingInterface_ReturnsObject()
    {
      // Arrange
      var (kernel, _) = await CreateKernelWithMockPack();

      // Act — intentionally testing the non-generic SysCtl(string, Type) overload
#pragma warning disable CA2263
      var result = kernel.SysCtl("test", typeof(IContainerDriver));
#pragma warning restore CA2263

      // Assert
      Assert.NotNull(result);
      Assert.IsAssignableFrom<IContainerDriver>(result);
    }

    [Fact]
    public async Task SysCtlByType_NonExistingInterface_ThrowsInterfaceNotSupported()
    {
      // Arrange
      var (kernel, _) = await CreateKernelWithMockPack();

      // Act & Assert — intentionally testing the non-generic SysCtl(string, Type) overload
#pragma warning disable CA2263
      Assert.Throws<InterfaceNotSupportedException>(() =>
          kernel.SysCtl("test", typeof(IDisposable)));
#pragma warning restore CA2263
    }

    [Fact]
    public async Task TrySysCtl_CustomInterface_AfterRegistration_Succeeds()
    {
      // Arrange
      var (kernel, pack) = await CreateKernelWithMockPack();
      var mockCustom = new Moq.Mock<ICustomKernelInterface>();
      pack.RegisterCustomDriver(mockCustom.Object);

      // Act
      var result = kernel.TrySysCtl<ICustomKernelInterface>("test", out var instance);

      // Assert
      Assert.True(result);
      Assert.NotNull(instance);
    }

    public interface ICustomKernelInterface
    {
      void Custom();
    }

    private class ThrowingUnsupportedPack : IDriverPack, IAsyncDisposable
    {
      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public T SysCtl<T>(string driverId) where T : class =>
          throw new InterfaceNotSupportedException(driverId, typeof(T).Name);

      public object SysCtl(string driverId, Type interfaceType) =>
          throw new InterfaceNotSupportedException(driverId, interfaceType.Name);

      public bool TrySysCtl<T>(string driverId, out T instance) where T : class
      {
        instance = null!;
        return false;
      }

      public virtual bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        throw new InterfaceNotSupportedException("test", interfaceType.Name);
      }

      public IReadOnlyCollection<Type> GetSupportedInterfaces() =>
          [];

      public virtual ValueTask DisposeAsync() =>
          ValueTask.CompletedTask;
    }

    private sealed class DisposableResolvingPack : ThrowingUnsupportedPack
    {
      private readonly Moq.Mock<IContainerDriver> _containerDriver = new();

      public bool Disposed { get; private set; }

      public override bool TryResolve(Type interfaceType, out object implementation)
      {
        if (interfaceType == typeof(IContainerDriver))
        {
          implementation = _containerDriver.Object;
          return true;
        }

        implementation = null!;
        return false;
      }

      public override ValueTask DisposeAsync()
      {
        Disposed = true;
        return ValueTask.CompletedTask;
      }
    }

    private sealed class ThrowingResolverDriver : IDriver, IDriverInterfaceResolver
    {
      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        throw new InterfaceNotSupportedException("test", interfaceType.Name);
      }

      public IReadOnlyCollection<Type> GetSupportedInterfaces() =>
          [];
    }
  }
}
