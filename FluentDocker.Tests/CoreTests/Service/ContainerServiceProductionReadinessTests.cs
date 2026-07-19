using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public partial class ContainerServiceProductionReadinessTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task StartAsync_WhenContainerExitsImmediately_LeavesStateStopped()
    {
      MockPack
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: false);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.StartAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }

    [Fact]
    public async Task ExportOnDispose_WithExplode_ExtractsArchiveToDirectory()
    {
      var sourceDir = Path.Combine(".out", "export-source");
      var tarPath = Path.Combine(".out", "export.tar");
      var destinationDir = Path.Combine(".out", "export-destination");
      Directory.CreateDirectory(sourceDir);
      Directory.CreateDirectory(Path.GetDirectoryName(tarPath)!);
      if (Directory.Exists(destinationDir))
        Directory.Delete(destinationDir, recursive: true);
      if (File.Exists(tarPath))
        File.Delete(tarPath);
      await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "data",
          TestContext.Current.CancellationToken);
      TarFile.CreateFromDirectory(sourceDir, tarPath, includeBaseDirectory: false);
      var archive = await File.ReadAllBytesAsync(tarPath, TestContext.Current.CancellationToken);

      MockPack
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .Setup(d => d.ExportAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, string, CancellationToken>((_, _, path, _) =>
              File.WriteAllBytes(path, archive))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          stopOnDispose: false,
          deleteOnDispose: true,
          lifecycleHooks:
          [
            new LifecycleHook
            {
              Type = LifecycleHookType.Export,
              TriggerState = ServiceRunningState.Removing,
              HostPath = destinationDir,
              Explode = true,
              Condition = _ => true
            }
          ]);

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal("data", await File.ReadAllTextAsync(
          Path.Combine(destinationDir, "file.txt"),
          TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDriverFails_PreservesErrorContext()
    {
      var context = new ErrorContext("ExecContainer") { DriverId = DriverId };
      MockPack.ContainerDriver
          .Setup(d => d.ExecAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              It.IsAny<ExecConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ExecResult>.Fail(
              "exec failed",
              ErrorCodes.Container.ExecFailed,
              context));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      var error = await Assert.ThrowsAsync<DriverException>(() =>
          service.ExecuteAsync("false", TestContext.Current.CancellationToken));

      Assert.Same(context, error.Context);
    }

    [Fact]
    public async Task ExportOnDispose_WithExplodeAndInvalidArchive_Propagates()
    {
      MockPack
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .Setup(d => d.ExportAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, string, CancellationToken>((_, _, path, _) =>
              File.WriteAllBytes(path, [1, 2, 3]))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          stopOnDispose: false,
          deleteOnDispose: true,
          lifecycleHooks:
          [
            new LifecycleHook
            {
              Type = LifecycleHookType.Export,
              TriggerState = ServiceRunningState.Removing,
              HostPath = Path.Combine(".out", "bad-export"),
              Explode = true,
              Condition = _ => true
            }
          ]);

      await Assert.ThrowsAsync<EndOfStreamException>(() =>
          service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_WithDeleteNamedVolumeOnDispose_RemovesNamedVolumeAfterContainer()
    {
      MockPack.SetupContainerRemove().SetupVolumeRemove();
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            Mounts = [new ContainerMount { Name = "orders-data" }]
          }));
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          stopOnDispose: false, deleteOnDispose: true, deleteNamedVolumeOnDispose: true);

      await service.DisposeAsync();

      MockPack.ContainerDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "container-123", true, false,
          It.IsAny<CancellationToken>()), Times.Once);
      MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "orders-data", false,
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WithDeleteNamedVolumeOnDispose_SkipsAnonymousAndBindMounts()
    {
      const string AnonymousName = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
      MockPack.SetupContainerRemove().SetupVolumeRemove();
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            Mounts =
            [
              new ContainerMount { Name = AnonymousName },
              new ContainerMount { Name = "", Source = "/host/path" }
            ]
          }));
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          stopOnDispose: false, deleteOnDispose: true, deleteNamedVolumeOnDispose: true);

      await service.DisposeAsync();

      MockPack.ContainerDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "container-123", true, false,
          It.IsAny<CancellationToken>()), Times.Once);
      MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_WithoutDeleteNamedVolumeOnDispose_DoesNotRemoveNamedVolume()
    {
      MockPack.SetupContainerRemove().SetupVolumeRemove();
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          stopOnDispose: false, deleteOnDispose: true, deleteNamedVolumeOnDispose: false);

      await service.DisposeAsync();

      MockPack.ContainerDriver.Verify(d => d.InspectAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_WithDeleteNamedVolumeOnDisposeAndNoVolumeDriver_RemovesContainer()
    {
      var pack = new ContainerOnlyDriverPack();
      await pack.InitializeAsync(new DriverContext(DriverId), TestContext.Current.CancellationToken);
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance),
          NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(DriverId, pack, new DriverContext(DriverId), TestContext.Current.CancellationToken);
      kernel.SetDefaultDriver(DriverId);
      pack.ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<bool>(), It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(
          kernel, DriverId, "container-123", "alpine", "test",
          stopOnDispose: false, deleteOnDispose: true, deleteNamedVolumeOnDispose: true);

      await service.DisposeAsync();

      pack.ContainerDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "container-123", true, false,
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ContainerService_ConcurrentStop_FiresEachStateChangeOnce()
    {
      const int Iterations = 100;
      const int Callers = 16;

      for (var iteration = 0; iteration < Iterations; iteration++)
      {
        var enteredStop = 0;
        var releaseStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MockPack.ContainerDriver
            .Setup(d => d.StopAsync(
                It.IsAny<DriverContext>(),
                "container-123",
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns<DriverContext, string, int?, CancellationToken>(async (_, _, _, _) =>
            {
              Interlocked.Increment(ref enteredStop);
              await releaseStop.Task.ConfigureAwait(false);
              return CommandResponse<Unit>.Ok(Unit.Default);
            });
        var service = new ContainerService(
            Kernel, DriverId, "container-123", "alpine", "test",
            initialState: ServiceRunningState.Running);
        var stopping = 0;
        var stopped = 0;
        service.StateChange += (_, args) =>
        {
          if (args.State == ServiceRunningState.Stopping)
            Interlocked.Increment(ref stopping);
          if (args.State == ServiceRunningState.Stopped)
            Interlocked.Increment(ref stopped);
        };

        var tasks = new Task[Callers];
        for (var i = 0; i < tasks.Length; i++)
        {
          tasks[i] = Task.Run(
              () => service.StopAsync(TestContext.Current.CancellationToken),
              TestContext.Current.CancellationToken);
        }

        for (var i = 0; i < 500 && Volatile.Read(ref enteredStop) < Callers; i++)
          await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(Callers, Volatile.Read(ref enteredStop));

        releaseStop.SetResult();
        await Task.WhenAll(tasks).WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref stopping));
        Assert.Equal(1, Volatile.Read(ref stopped));
      }
    }

    [Fact]
    public async Task StartAsync_AfterDispose_ThrowsAndDoesNotCallDriver()
    {
      MockPack.SetupContainerStart()
          .SetupContainerInspect("container-123", running: true);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          service.StartAsync(TestContext.Current.CancellationToken));

      MockPack.ContainerDriver.Verify(d => d.StartAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void IServiceCapabilities_CanHook_MatchesHookSupport()
    {
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var services = new (IServiceCapabilities Service, bool Expected)[]
      {
        (new ContainerService(Kernel, DriverId, "container-123", "alpine", "test"), true),
        (new ModelService(Kernel, DriverId, ModelReference.Parse("ai/smollm2"), runner.Object, null!, true), true),
        (new ComposeService(Kernel, DriverId, [], "project"), true),
        (new PodService(Kernel, DriverId, "pod-id", "pod"), true),
        (new ImageService(Kernel, DriverId, "image-1", "repo", "tag"), true),
        (new NetworkService(Kernel, DriverId, "net-1", "net"), true),
        (new VolumeService(Kernel, DriverId, "vol-1", "local"), true),
        (new HostService(Kernel, DriverId, "host"), false)
      };

      foreach (var (service, expected) in services)
        Assert.Equal(expected, service.CanHook);
    }

    [Fact]
    public async Task StartAsync_DoesNotPopulateInspectCache()
    {
      MockPack.ContainerDriver
          .Setup(d => d.StartAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      MockPack.ContainerDriver
          .SetupSequence(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            Name = "from-start",
            State = new ContainerState { Running = true, Status = "running" }
          }))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            Name = "fresh-inspect",
            State = new ContainerState { Running = true, Status = "running" }
          }));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.StartAsync(TestContext.Current.CancellationToken);
      var inspected = await service.InspectAsync(TestContext.Current.CancellationToken);

      Assert.Equal("fresh-inspect", inspected.Name);
      MockPack.ContainerDriver.Verify(d => d.InspectAsync(
          It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // S-M2: OperationCanceledException from a canceled dispose token must stop the per-volume
    // cleanup loop instead of being swallowed and retried against every remaining named volume.
    [Fact]
    public async Task DisposeAsync_NamedVolumeCleanupThrowsOperationCanceled_StopsLoopInsteadOfCallingEveryVolume()
    {
      MockPack.SetupContainerRemove();
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            Mounts =
            [
              new ContainerMount { Name = "orders-data" },
              new ContainerMount { Name = "orders-log" },
              new ContainerMount { Name = "orders-cache" }
            ]
          }));
      MockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          stopOnDispose: false, deleteOnDispose: true, deleteNamedVolumeOnDispose: true);

      await service.DisposeAsync();

      MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class ContainerOnlyDriverPack : IDriverPack
    {
      private bool _initialized;

      public Mock<IContainerDriver> ContainerDriver { get; } = new Mock<IContainerDriver>();
      public DriverType Type => DriverType.DockerCli;
      public RuntimeType Runtime => RuntimeType.Docker;

      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default)
      {
        _initialized = true;
        return Task.CompletedTask;
      }

      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public T SysCtl<T>(string driverId) where T : class
      {
        if (TrySysCtl<T>(driverId, out var instance))
          return instance;

        throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
      }

      public object SysCtl(string driverId, Type interfaceType)
      {
        if (TryResolve(interfaceType, out var implementation))
          return implementation;

        throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
      }

      public bool TrySysCtl<T>(string driverId, out T instance) where T : class
      {
        if (!_initialized)
          throw new InvalidOperationException("ContainerOnlyDriverPack not initialized.");

        if (typeof(T) == typeof(IContainerDriver))
        {
          instance = (T)(object)ContainerDriver.Object;
          return true;
        }

        instance = null!;
        return false;
      }

      public bool TryResolve(Type interfaceType, out object implementation)
      {
        if (!_initialized)
          throw new InvalidOperationException("ContainerOnlyDriverPack not initialized.");

        if (interfaceType == typeof(IContainerDriver))
        {
          implementation = ContainerDriver.Object;
          return true;
        }

        implementation = null!;
        return false;
      }

      public IReadOnlyCollection<Type> GetSupportedInterfaces() => [typeof(IContainerDriver)];
    }
  }
}
