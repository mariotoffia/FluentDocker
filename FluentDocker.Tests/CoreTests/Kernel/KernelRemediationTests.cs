using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  [Trait("Category", "Unit")]
  public class KernelRemediationTests
  {
    [Fact]
    public async Task BuildAsync_CalledTwice_WithBuiltInPackFactory_Throws()
    {
      var builder = FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerApi("api", d => d.AsDefault());

      await using var first = await builder.BuildAsync(TestContext.Current.CancellationToken);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          builder.BuildAsync(TestContext.Current.CancellationToken));

      Assert.NotNull(first.SysCtl<IContainerDriver>("api"));
    }

    [Fact]
    public async Task BuildAsync_AfterFailedBuild_RejectsBuilderReuse()
    {
      var failOnce = new FailOnceDriverPack();
      var builder = FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerApi("api", d => d.AsDefault())
          .WithDriver("custom", d => d.UseCustomDriverPack(failOnce));

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          builder.BuildAsync(TestContext.Current.CancellationToken));

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          builder.BuildAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_DuringDriverRegistration_DisposesInitializedDriver()
    {
      var initializeStarted = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var completeInitialize = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var disposed = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var driver = new Mock<IDriver>();
      driver.SetupGet(d => d.Type).Returns(DriverType.DockerCli);
      driver.SetupGet(d => d.Runtime).Returns(RuntimeType.Docker);
      driver
          .Setup(d => d.InitializeAsync(
              It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            initializeStarted.SetResult();
            await completeInitialize.Task.ConfigureAwait(false);
          });
      driver.As<IAsyncDisposable>()
          .Setup(d => d.DisposeAsync())
          .Callback(() => disposed.SetResult())
          .Returns(ValueTask.CompletedTask);
      var registry = new DriverRegistry(NullLoggerFactory.Instance);

      var register = registry.RegisterAsync(
          "slow", driver.Object, new DriverContext("slow"),
          TestContext.Current.CancellationToken);
      await initializeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

      var dispose = registry.DisposeAsync().AsTask();
      Assert.False(dispose.IsCompleted);

      completeInitialize.SetResult();

      await Assert.ThrowsAsync<ObjectDisposedException>(() => register);
      await dispose.WaitAsync(TestContext.Current.CancellationToken);
      await disposed.Task.WaitAsync(TestContext.Current.CancellationToken);
      driver.As<IAsyncDisposable>().Verify(d => d.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      await registry.DisposeAsync();
      var driver = new Mock<IDriver>();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          registry.RegisterAsync(
              "late", driver.Object, new DriverContext("late"),
              TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unregister_DisposesRemovedPack_AndClearsDefaultDriver()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var pack = new Mock<IDriverPack>();
      pack.SetupGet(p => p.Type).Returns(DriverType.DockerCli);
      pack.SetupGet(p => p.Runtime).Returns(RuntimeType.Docker);
      pack
          .Setup(p => p.InitializeAsync(
              It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      pack.As<IAsyncDisposable>()
          .Setup(p => p.DisposeAsync())
          .Returns(ValueTask.CompletedTask);
      await registry.RegisterDriverPackAsync(
          "default", pack.Object, new DriverContext("default"),
          TestContext.Current.CancellationToken);

      registry.Unregister("default");

      pack.As<IAsyncDisposable>().Verify(p => p.DisposeAsync(), Times.Once);
      Assert.False(registry.IsRegistered("default"));
      Assert.Null(registry.GetDefaultDriverId());
    }

    [Fact]
    public async Task Unregister_DuringDispose_DisposesPackOnlyOnce()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var pack = new BlockingDisposePack();
      await registry.RegisterDriverPackAsync(
          "docker", pack, new DriverContext("docker"),
          TestContext.Current.CancellationToken);

      var dispose = registry.DisposeAsync().AsTask();
      await pack.DisposeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

      Assert.Throws<ObjectDisposedException>(() => registry.Unregister("docker"));
      pack.CompleteDispose.SetResult();
      await dispose.WaitAsync(TestContext.Current.CancellationToken);

      Assert.Equal(1, pack.DisposeCount);
    }

    [Fact]
    public async Task TrySysCtl_WhenPackReturnsWrongType_ReturnsFalse()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "bad", new WrongTypeDriverPack(), new DriverContext("bad"),
          TestContext.Current.CancellationToken);

      var found = kernel.TrySysCtl<IContainerDriver>("bad", out var instance);

      Assert.False(found);
      Assert.Null(instance);
      Assert.Throws<InterfaceNotSupportedException>(() =>
          kernel.SysCtl<IContainerDriver>("bad"));
    }

    [Fact]
    public async Task TrySysCtl_WhenPackFallbackReturnsWrongType_ReturnsFalse()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "bad", new WrongTypeFallbackDriverPack(), new DriverContext("bad"),
          TestContext.Current.CancellationToken);

      var found = kernel.TrySysCtl<IContainerDriver>("bad", out var instance);

      Assert.False(found);
      Assert.Null(instance);
      Assert.Throws<InterfaceNotSupportedException>(() =>
          kernel.SysCtl<IContainerDriver>("bad"));
    }

    [Fact]
    public async Task RegisterDriverPackAsync_WhenInitializeThrows_DisposesFailingPack()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var pack = new Mock<IDriverPack>();
      pack
          .Setup(p => p.InitializeAsync(
              It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("boom"));
      pack.As<IAsyncDisposable>()
          .Setup(p => p.DisposeAsync())
          .Returns(ValueTask.CompletedTask);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          registry.RegisterDriverPackAsync(
              "bad", pack.Object, new DriverContext("bad"),
              TestContext.Current.CancellationToken));

      pack.As<IAsyncDisposable>().Verify(p => p.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WhenInitializeThrows_DisposesFailingDriver()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var driver = new Mock<IDriver>();
      driver
          .Setup(d => d.InitializeAsync(
              It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("boom"));
      driver.As<IAsyncDisposable>()
          .Setup(d => d.DisposeAsync())
          .Returns(ValueTask.CompletedTask);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          registry.RegisterAsync(
              "bad", driver.Object, new DriverContext("bad"),
              TestContext.Current.CancellationToken));

      driver.As<IAsyncDisposable>().Verify(d => d.DisposeAsync(), Times.Once);
    }

    [Fact]
    public void SysCtl_WithNullArguments_ThrowsNamedArgumentNullException()
    {
      var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

      var driverId = Assert.Throws<ArgumentNullException>(() =>
          kernel.SysCtl(null!, typeof(IContainerDriver)));
      var interfaceType = Assert.Throws<ArgumentNullException>(() =>
          kernel.SysCtl("driver", null!));

      Assert.Equal("driverId", driverId.ParamName);
      Assert.Equal("interfaceType", interfaceType.ParamName);
    }

    private class FailOnceDriverPack : IDriverPack
    {
      private int _initializationCount;

      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public virtual Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default)
      {
        if (Interlocked.Increment(ref _initializationCount) == 1)
          throw new InvalidOperationException("first initialization fails");
        return Task.CompletedTask;
      }

      public Task<DriverCapabilities> GetCapabilitiesAsync(
          CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public T SysCtl<T>(string driverId) where T : class =>
          throw new InterfaceNotSupportedException(driverId, typeof(T).Name);

      public virtual object SysCtl(string driverId, Type interfaceType) =>
          throw new InterfaceNotSupportedException(driverId, interfaceType.Name);

      public bool TrySysCtl<T>(string driverId, out T instance) where T : class
      {
        instance = null!;
        return false;
      }

      public virtual bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        return false;
      }

      public System.Collections.Generic.IReadOnlyCollection<Type> GetSupportedInterfaces() =>
          [];
    }

    private sealed class WrongTypeDriverPack : FailOnceDriverPack
    {
      public override Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public override bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = "not a container driver";
        return true;
      }
    }

    private sealed class WrongTypeFallbackDriverPack : FailOnceDriverPack
    {
      public override Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public override bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        return false;
      }

      public override object SysCtl(string driverId, Type interfaceType) =>
          "not a container driver";
    }

    private sealed class BlockingDisposePack : FailOnceDriverPack, IAsyncDisposable
    {
      public TaskCompletionSource DisposeStarted { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      public TaskCompletionSource CompleteDispose { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      public int DisposeCount => _disposeCount;

      private int _disposeCount;

      public override Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public async ValueTask DisposeAsync()
      {
        Interlocked.Increment(ref _disposeCount);
        DisposeStarted.SetResult();
        await CompleteDispose.Task.ConfigureAwait(false);
      }
    }
  }
}
