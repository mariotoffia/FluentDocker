using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  [Trait("Category", "Unit")]
  public class KernelDriverPortProductionReadinessTests
  {
    [Fact]
    public void TryMethods_HaveNotNullWhenTrueOutContracts()
    {
      AssertNotNullWhenTrue(typeof(ISysCtl), nameof(ISysCtl.TrySysCtl));
      AssertNotNullWhenTrue(typeof(IDriverRegistry), nameof(IDriverRegistry.TryGetDriver));
      AssertNotNullWhenTrue(typeof(IDriverRegistry), nameof(IDriverRegistry.TryGetDriverPack));
      AssertNotNullWhenTrue(typeof(IDriverInterfaceResolver), nameof(IDriverInterfaceResolver.TryResolve));
      AssertNotNullWhenTrue(typeof(DriverPackBase), "TryResolveSysCtl", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    [Fact]
    public void DriverPackBase_NullMappedInterface_IsTreatedAsUnsupported()
    {
      // DRV-11: a subclass can write null straight into Drivers; TryResolve and
      // TryResolveSysCtl must report the interface unsupported instead of returning
      // true with a null instance (which would violate [NotNullWhen(true)]).
      var pack = new NullInsertingPackBase();

      Assert.False(pack.TryResolve(typeof(IImageDriver), out var implementation));
      Assert.Null(implementation);
      Assert.False(pack.TryResolveImageDriver(out var typed));
      Assert.Null(typed);
    }

    [Fact]
    public void DriverPackBase_RegisteredDriver_StillResolves()
    {
      var pack = new NullInsertingPackBase();

      Assert.True(pack.TryResolve(typeof(IContainerDriver), out var implementation));
      Assert.Same(pack.ContainerDriver, implementation);
    }

    [Fact]
    public async Task TrySysCtl_WhenInterfaceUnsupported_ReturnsFalseAndNull()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var pack = new FallbackThrowsPack();
      await kernel.RegisterDriverPackAsync(
          "pack", pack, new DriverContext("pack"), TestContext.Current.CancellationToken);

      var found = kernel.TrySysCtl<IImageDriver>("pack", out var imageDriver);

      // KRN-MAJ-7 removed the redundant driverId-based SysCtl fallback, so the pack's TryResolve
      // miss is authoritative and the (never-invoked) fallback is not called.
      Assert.False(found);
      Assert.Null(imageDriver);
      Assert.Equal(0, pack.FallbackCalls);
    }

    [Fact]
    public async Task TrySysCtl_WhenInterfaceSupported_ReturnsTrueAndNonNull()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var pack = new ResolvingPack();
      await kernel.RegisterDriverPackAsync(
          "pack", pack, new DriverContext("pack"), TestContext.Current.CancellationToken);

      var found = kernel.TrySysCtl<IImageDriver>("pack", out var imageDriver);

      Assert.True(found);
      Assert.NotNull(imageDriver);
      Assert.Same(pack.ImageDriver.Object, imageDriver);
    }

    [Fact]
    public async Task SysCtl_WhenDefaultDriverCleared_ThrowsClearDefaultDriverError()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "pack", new ResolvingPack(), new DriverContext("pack"), TestContext.Current.CancellationToken);
      await kernel.UnregisterDriverAsync("pack", TestContext.Current.CancellationToken);

      var ex = Assert.Throws<InvalidOperationException>(() =>
          kernel.SysCtl<IImageDriver>(kernel.DefaultDriverId));

      Assert.Contains("No default driver configured", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterAsync_WhenContextDriverIdDiffers_Throws()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var driver = new Mock<IDriver>();
      driver.SetupGet(d => d.Type).Returns(DriverType.Custom);
      driver.SetupGet(d => d.Runtime).Returns(RuntimeType.Unknown);

      var ex = await Assert.ThrowsAsync<DriverContextIdMismatchException>(() =>
          registry.RegisterAsync(
              "actual", driver.Object, new DriverContext("other"),
              TestContext.Current.CancellationToken));

      Assert.Equal("context", ex.ParamName);
    }

    [Fact]
    public async Task RegisterAsync_WhenContextDriverIdMissing_AutoFillsCopyOnly()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var seenContext = new TaskCompletionSource<DriverContext>(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var context = new DriverContext();
      var driver = new Mock<IDriver>();
      driver.SetupGet(d => d.Type).Returns(DriverType.Custom);
      driver.SetupGet(d => d.Runtime).Returns(RuntimeType.Unknown);
      driver
          .Setup(d => d.InitializeAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, CancellationToken>((ctx, _) => seenContext.SetResult(ctx))
          .Returns(Task.CompletedTask);

      await registry.RegisterAsync("actual", driver.Object, context, TestContext.Current.CancellationToken);

      var initializedContext = await seenContext.Task.WaitAsync(TestContext.Current.CancellationToken);
      Assert.Equal("actual", initializedContext.DriverId);
      Assert.Null(context.DriverId);
    }

    [Fact]
    public async Task RegisterAsync_DoesNotClobberProvidedLoggerFactory()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var providedLoggerFactory = new LoggerFactory();
      var context = new DriverContext("driver")
      {
        LoggerFactory = providedLoggerFactory
      };
      var seenLoggerFactory = new TaskCompletionSource<ILoggerFactory>(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var driver = new Mock<IDriver>();
      driver.SetupGet(d => d.Type).Returns(DriverType.Custom);
      driver.SetupGet(d => d.Runtime).Returns(RuntimeType.Unknown);
      driver
          .Setup(d => d.InitializeAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, CancellationToken>((ctx, _) => seenLoggerFactory.SetResult(ctx.LoggerFactory))
          .Returns(Task.CompletedTask);

      await registry.RegisterAsync("driver", driver.Object, context, TestContext.Current.CancellationToken);

      Assert.Same(providedLoggerFactory, await seenLoggerFactory.Task.WaitAsync(TestContext.Current.CancellationToken));
      Assert.Same(providedLoggerFactory, context.LoggerFactory);
    }

    [Fact]
    public async Task AttachResultDisposeAsync_WhenCalledTwice_DisposesStreamsOnce()
    {
      var input = new ThrowOnSecondDisposeStream();
      var output = new ThrowOnSecondDisposeStream();
      var error = new ThrowOnSecondDisposeStream();
      var result = new AttachResult
      {
        InputStream = input,
        OutputStream = output,
        ErrorStream = error,
        IsConnected = true
      };

      await result.DisposeAsync();
      await result.DisposeAsync();

      Assert.False(result.IsConnected);
      Assert.Equal(1, input.DisposeCount);
      Assert.Equal(1, output.DisposeCount);
      Assert.Equal(1, error.DisposeCount);
      Assert.Null(result.KillError);
      // ponytail: kill-race coverage needs a real attach process; keep it integration-level.
    }

    private static void AssertNotNullWhenTrue(
        Type type,
        string methodName,
        BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public)
    {
      var method = type.GetMethod(methodName, bindingFlags);
      Assert.NotNull(method);
      var outParameter = Assert.Single(method.GetParameters(), p => p.IsOut);
      var attribute = outParameter.GetCustomAttribute<NotNullWhenAttribute>();
      Assert.NotNull(attribute);
      Assert.True(attribute.ReturnValue);
    }

    private sealed class NullInsertingPackBase : DriverPackBase
    {
      public IContainerDriver ContainerDriver { get; } = new Mock<IContainerDriver>().Object;

      public NullInsertingPackBase()
      {
        Drivers[typeof(IImageDriver)] = null!;
        Drivers[typeof(IContainerDriver)] = ContainerDriver;
      }

      public bool TryResolveImageDriver(out IImageDriver? instance) =>
          TryResolveSysCtl(out instance);
    }

    private class FallbackThrowsPack : IDriverPack
    {
      public int FallbackCalls;

      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public object SysCtl(string driverId, Type interfaceType)
      {
        Interlocked.Increment(ref FallbackCalls);
        throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
      }

      public virtual bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        return false;
      }

      public System.Collections.Generic.IReadOnlyCollection<Type> GetSupportedInterfaces() =>
          [];
    }

    private sealed class ResolvingPack : FallbackThrowsPack
    {
      public Mock<IImageDriver> ImageDriver { get; } = new();

      public override bool TryResolve(Type interfaceType, out object implementation)
      {
        if (interfaceType == typeof(IImageDriver))
        {
          implementation = ImageDriver.Object;
          return true;
        }

        implementation = null!;
        return false;
      }
    }

    private sealed class ThrowOnSecondDisposeStream : MemoryStream
    {
      public int DisposeCount { get; private set; }

      protected override void Dispose(bool disposing)
      {
        DisposeCount++;
        if (DisposeCount > 1)
          throw new InvalidOperationException("disposed twice");
        base.Dispose(disposing);
      }
    }
  }
}
