using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  [Trait("Category", "Unit")]
  public sealed class KernelDriverInfrastructureChunk4Tests
  {
    [Fact]
    public async Task SysCtl_WhenPackResolverMisses_DelegatesToPackSysCtl()
    {
      var marker = new Marker();
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "pack", new SysCtlFallbackPack(marker), new DriverContext("pack"),
          TestContext.Current.CancellationToken);

      var resolved = kernel.SysCtl<IMarker>("pack");

      Assert.Same(marker, resolved);
    }

    [Fact]
    public async Task TrySysCtl_WhenPackSysCtlDoesNotSupportInterface_ReturnsFalse()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "pack", new UnsupportedFallbackPack(), new DriverContext("pack"),
          TestContext.Current.CancellationToken);

      var found = kernel.TrySysCtl<IMarker>("pack", out var resolved);

      Assert.False(found);
      Assert.Null(resolved);
    }

    [Fact]
    public void SysCtl_WithWhitespaceDriverId_ThrowsClearDefaultDriverError()
    {
      var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

      var ex = Assert.Throws<InvalidOperationException>(() =>
          kernel.SysCtl("   ", typeof(IMarker)));

      Assert.Contains("No default driver configured", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterAsync_DoesNotBlockIndependentRegistrationDuringSlowInitialize()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      using var slowCts = new CancellationTokenSource();
      var slow = new TestDriver
      {
        InitializeAsyncFunc = (_, _) => Task.Delay(TimeSpan.FromSeconds(5), slowCts.Token)
      };
      var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      slow.InitializeAsyncFunc = async (_, ct) =>
      {
        started.SetResult();
        await Task.Delay(TimeSpan.FromSeconds(5), slowCts.Token).ConfigureAwait(false);
      };
      var slowRegister = registry.RegisterAsync(
          "slow", slow, new DriverContext("slow"), TestContext.Current.CancellationToken);
      await started.Task.WaitAsync(TestContext.Current.CancellationToken);

      await registry.RegisterAsync(
          "fast", new TestDriver(), new DriverContext("fast"),
          TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(250));

      Assert.True(registry.IsRegistered("fast"));
      await registry.DisposeAsync();
      await slowCts.CancelAsync();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slowRegister);
    }

    [Fact]
    public async Task DisposeAsync_UsesOneTotalTimeoutBudget()
    {
      var registry = new FastDisposeBudgetRegistry();
      var slow = new BlockingDisposeDriver();
      var fast = new BlockingDisposeDriver();
      await registry.RegisterAsync("slow", slow, new DriverContext("slow"), TestContext.Current.CancellationToken);
      await registry.RegisterAsync("fast", fast, new DriverContext("fast"), TestContext.Current.CancellationToken);

      var dispose = registry.DisposeAsync().AsTask();
      await slow.DisposeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
      await dispose.WaitAsync(TimeSpan.FromSeconds(2));

      Assert.Equal(1, slow.DisposeCount);
      Assert.InRange(fast.DisposeCount, 0, 1);
    }

    [Fact]
    public async Task DisposeAsync_WhenRegistrationLockUnavailable_ThrowsAndAllowsRetry()
    {
      var registry = new FastDisposeBudgetRegistry();
      var field = typeof(DriverRegistry).GetField(
          "_registrationLock", BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(field);
      var semaphore = (SemaphoreSlim)field.GetValue(registry)!;
      await semaphore.WaitAsync(TestContext.Current.CancellationToken);
      try
      {
        await Assert.ThrowsAsync<TimeoutException>(() => registry.DisposeAsync().AsTask());
      }
      finally
      {
        semaphore.Release();
      }

      await registry.DisposeAsync();
      Assert.True(GetIsDisposeComplete(registry));
    }

    [Fact]
    public async Task DisposeAsync_WhenLockTimesOut_DoesNotResurrectDisposedState()
    {
      var registry = new FastDisposeBudgetRegistry();
      await registry.RegisterAsync(
          "d", new TestDriver(), new DriverContext("d"), TestContext.Current.CancellationToken);
      var field = typeof(DriverRegistry).GetField(
          "_registrationLock", BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(field);
      var semaphore = (SemaphoreSlim)field.GetValue(registry)!;
      await semaphore.WaitAsync(TestContext.Current.CancellationToken);
      try
      {
        await Assert.ThrowsAsync<TimeoutException>(() => registry.DisposeAsync().AsTask());

        // A disposal that could not acquire the lock must leave the registry disposed, not
        // resurrect it to "alive": an off-lock reader must still observe ObjectDisposedException
        // (otherwise a concurrent disposer tearing drivers down could hand out a half-disposed one).
        Assert.Throws<ObjectDisposedException>(() => registry.GetDriver("d"));
      }
      finally
      {
        semaphore.Release();
      }
    }

    [Fact]
    public void DriverContext_CloneWith_CopiesMetadataDictionary()
    {
      var context = new DriverContext("source")
      {
        Metadata = new Dictionary<string, string> { ["original"] = "value" },
        Host = "tcp://host:2375"
      };

      var clone = context.CloneWith("target", NullLoggerFactory.Instance);
      context.Metadata["original"] = "changed";

      Assert.Equal("target", clone.DriverId);
      Assert.Equal("tcp://host:2375", clone.Host);
      Assert.Equal("value", clone.Metadata["original"]);
    }

    private interface IMarker
    {
    }

    private sealed class Marker : IMarker
    {
    }

    private class TestDriver : IDriver, IAsyncDisposable
    {
      public Func<DriverContext, CancellationToken, Task> InitializeAsyncFunc { get; set; } =
          (_, _) => Task.CompletedTask;
      public Func<ValueTask> DisposeAsyncFunc { get; set; } = () => ValueTask.CompletedTask;
      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;
      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());
      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);
      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default) =>
          InitializeAsyncFunc(context, cancellationToken);
      public ValueTask DisposeAsync() => DisposeAsyncFunc();
    }

    private sealed class BlockingDisposeDriver : TestDriver
    {
      private int _disposeCount;
      public TaskCompletionSource DisposeStarted { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      public TaskCompletionSource CompleteDispose { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      public int DisposeCount => _disposeCount;

      public BlockingDisposeDriver() => DisposeAsyncFunc = async () =>
                                             {
                                               Interlocked.Increment(ref _disposeCount);
                                               DisposeStarted.SetResult();
                                               await CompleteDispose.Task.ConfigureAwait(false);
                                             };
    }

    private sealed class FastDisposeBudgetRegistry : DriverRegistry
    {
      public FastDisposeBudgetRegistry() : base(NullLoggerFactory.Instance)
      {
      }

      protected override TimeSpan DisposeBudget => TimeSpan.FromMilliseconds(100);
    }

    private static bool GetIsDisposeComplete(DriverRegistry registry)
    {
      var property = typeof(DriverRegistry).GetProperty(
          "IsDisposeComplete", BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(property);
      return (bool)property.GetValue(registry)!;
    }

    private abstract class BasePack : IDriverPack
    {
      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;
      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;
      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());
      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);
      public virtual T SysCtl<T>(string driverId) where T : class =>
          (T)SysCtl(driverId, typeof(T));
      public abstract object SysCtl(string driverId, Type interfaceType);
      public virtual bool TrySysCtl<T>(string driverId, out T instance) where T : class
      {
        try
        {
          instance = SysCtl<T>(driverId);
          return true;
        }
        catch (InterfaceNotSupportedException)
        {
          instance = null!;
          return false;
        }
      }
      public bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        return false;
      }
      public IReadOnlyCollection<Type> GetSupportedInterfaces() => [];
    }

    private sealed class SysCtlFallbackPack(IMarker marker) : BasePack
    {
      public override object SysCtl(string driverId, Type interfaceType)
      {
        if (interfaceType == typeof(IMarker))
          return marker;
        throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
      }
    }

    private sealed class UnsupportedFallbackPack : BasePack
    {
      public override object SysCtl(string driverId, Type interfaceType) =>
          throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
    }
  }
}
