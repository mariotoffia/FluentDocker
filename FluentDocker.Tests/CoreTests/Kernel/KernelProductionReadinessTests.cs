using System;
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
  public class KernelProductionReadinessTests
  {
    [Fact]
    public async Task BuildAsync_WhenCalledTwice_ThrowsAndLeavesFirstKernelUsable()
    {
      var pack = new CountingDriverPack();
      var builder = FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDriver("custom", d => d.UseCustomDriverPack(pack).AsDefault());

      await using var first = await builder.BuildAsync(TestContext.Current.CancellationToken);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          builder.BuildAsync(TestContext.Current.CancellationToken));

      Assert.True(first.IsDriverRegistered("custom"));
      Assert.Equal("custom", first.DefaultDriverId);
      Assert.Equal(1, pack.InitializeCount);
    }

    [Fact]
    public async Task DriverRegistry_ReadPathsAfterDispose_ThrowObjectDisposedException()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      await registry.RegisterDriverPackAsync(
          "custom", new CountingDriverPack(), new DriverContext("custom"),
          TestContext.Current.CancellationToken);

      await registry.DisposeAsync();

      Assert.Throws<ObjectDisposedException>(() => registry.TryGetDriver("custom", out _));
      Assert.Throws<ObjectDisposedException>(() => registry.TryGetDriverPack("custom", out _));
      Assert.Throws<ObjectDisposedException>(() => registry.IsDriverPack("custom"));
      Assert.Throws<ObjectDisposedException>(() => registry.GetContext("custom"));
      Assert.Throws<ObjectDisposedException>(() => registry.IsRegistered("custom"));
      Assert.Throws<ObjectDisposedException>(() => registry.GetAllDriverIds());
      Assert.Throws<ObjectDisposedException>(() => registry.GetDriversByType(DriverType.Custom));
      Assert.Throws<ObjectDisposedException>(() => registry.GetDriversByRuntime(RuntimeType.Unknown));
      Assert.Throws<ObjectDisposedException>(() => registry.GetDefaultDriverId());
    }

    [Fact]
    public async Task UnregisterDriverAsync_DisposesPackAndClearsDefaultDriver()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var pack = new CountingDriverPack();
      await kernel.RegisterDriverPackAsync(
          "custom", pack, new DriverContext("custom"),
          TestContext.Current.CancellationToken);

      await kernel.UnregisterDriverAsync("custom", TestContext.Current.CancellationToken);

      Assert.False(kernel.IsDriverRegistered("custom"));
      Assert.Null(kernel.DefaultDriverId);
      Assert.Equal(1, pack.DisposeAsyncCount);
    }

    [Fact]
    public async Task CapabilityChecks_WithPlainRegisteredDriver_UsesDriverCapabilities()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverAsync(
          "plain", new PlainDriver(new DriverCapabilities { SupportsContainers = true }),
          new DriverContext("plain"), TestContext.Current.CancellationToken);

      await CapabilityChecks.EnsureContainerSupportAsync(
          kernel, "plain", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DisposeAsync_DuringSlowRegistration_DisposesExistingPackImmediately()
    {
      var registry = new ShortTimeoutRegistry();
      await using var kernel = new FluentDockerKernel(registry, NullLoggerFactory.Instance);
      var existing = new CountingDriverPack();
      await kernel.RegisterDriverPackAsync(
          "existing", existing, new DriverContext("existing"),
          TestContext.Current.CancellationToken);
      var slow = new BlockingInitializeDriverPack();
      var register = kernel.RegisterDriverPackAsync(
          "slow", slow, new DriverContext("slow"),
          TestContext.Current.CancellationToken);
      await slow.InitializeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

      await kernel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

      Assert.Equal(1, existing.DisposeAsyncCount);

      slow.CompleteInitialize.SetResult();
      await Assert.ThrowsAsync<ObjectDisposedException>(() => register);
      Assert.Equal(1, existing.DisposeAsyncCount);
    }

    [Fact]
    public async Task DisposeAsync_WhenSyncPackDisposeHangs_UsesDisposalBudget()
    {
      var registry = new ShortTimeoutRegistry();
      var pack = new BlockingSyncDisposeDriverPack();
      await registry.RegisterDriverPackAsync(
          "blocking", pack, new DriverContext("blocking"),
          TestContext.Current.CancellationToken);

      try
      {
        await registry.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await pack.DisposeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
      }
      finally
      {
        pack.CompleteDispose.SetResult();
      }
    }

    [Fact]
    public void DisposeSurface_MatchesDisposeAsyncVirtualSurface()
    {
      var dispose = typeof(FluentDockerKernel).GetMethod(
          nameof(FluentDockerKernel.Dispose),
          BindingFlags.Instance | BindingFlags.Public);
      var disposeAsync = typeof(FluentDockerKernel).GetMethod(
          nameof(FluentDockerKernel.DisposeAsync),
          BindingFlags.Instance | BindingFlags.Public);

      Assert.NotNull(dispose);
      Assert.NotNull(disposeAsync);
      Assert.True(dispose.IsVirtual);
      Assert.True(disposeAsync.IsVirtual);
    }

    private sealed class CountingDriverPack : IDriverPack, IAsyncDisposable
    {
      public int InitializeCount;
      public int DisposeAsyncCount;

      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default)
      {
        Interlocked.Increment(ref InitializeCount);
        return Task.CompletedTask;
      }

      public Task<DriverCapabilities> GetCapabilitiesAsync(
          CancellationToken cancellationToken = default) =>
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

      public bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        return false;
      }

      public System.Collections.Generic.IReadOnlyCollection<Type> GetSupportedInterfaces() =>
          [];

      public ValueTask DisposeAsync()
      {
        Interlocked.Increment(ref DisposeAsyncCount);
        return ValueTask.CompletedTask;
      }
    }

    private sealed class PlainDriver(DriverCapabilities capabilities) : IDriver
    {
      private readonly DriverCapabilities _capabilities = capabilities;

      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task<DriverCapabilities> GetCapabilitiesAsync(
          CancellationToken cancellationToken = default) =>
          Task.FromResult(_capabilities);

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;
    }

    private sealed class ShortTimeoutRegistry : DriverRegistry
    {
      public ShortTimeoutRegistry() : base(NullLoggerFactory.Instance)
      {
      }

      protected override TimeSpan DisposeBudget => TimeSpan.FromMilliseconds(50);
    }

    private sealed class BlockingInitializeDriverPack : IDriverPack, IAsyncDisposable
    {
      public TaskCompletionSource InitializeStarted { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      public TaskCompletionSource CompleteInitialize { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      public int DisposeAsyncCount;

      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public async Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default)
      {
        InitializeStarted.SetResult();
        await CompleteInitialize.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
      }

      public Task<DriverCapabilities> GetCapabilitiesAsync(
          CancellationToken cancellationToken = default) =>
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

      public bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        return false;
      }

      public System.Collections.Generic.IReadOnlyCollection<Type> GetSupportedInterfaces() =>
          [];

      public ValueTask DisposeAsync()
      {
        Interlocked.Increment(ref DisposeAsyncCount);
        return ValueTask.CompletedTask;
      }
    }

    private sealed class BlockingSyncDisposeDriverPack : IDriverPack, IDisposable
    {
      public TaskCompletionSource DisposeStarted { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      public TaskCompletionSource CompleteDispose { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);

      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public Task<DriverCapabilities> GetCapabilitiesAsync(
          CancellationToken cancellationToken = default) =>
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

      public bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        return false;
      }

      public System.Collections.Generic.IReadOnlyCollection<Type> GetSupportedInterfaces() =>
          [];

      public void Dispose()
      {
        DisposeStarted.SetResult();
        CompleteDispose.Task.GetAwaiter().GetResult();
      }
    }
  }
}
