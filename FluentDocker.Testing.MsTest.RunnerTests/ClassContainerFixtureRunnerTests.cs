using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.MsTest;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace FluentDocker.Testing.MsTest.RunnerTests
{
  [TestClass]
  [TestCategory("Unit")]
  public class ClassContainerFixtureRunnerTests
      : MsTestClassContainerFixtureBase<ClassContainerFixtureRunnerTests>
  {
    private static readonly MockContainerDriverPack Pack = new();
    internal static readonly string ContainerName = $"mstest-class-fixture-{Guid.NewGuid():N}";
    private static string? _firstContainerId;
    internal static bool CleanupCompleted { get; private set; }
    internal static int RemoveCallsAfterCleanup { get; private set; }

    protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
        async () => (await MockContainerKernel.CreateAsync(Pack).ConfigureAwait(false)).kernel;

    protected override DockerResourceOptions? GetOptions() =>
        new() { Driver = DriverSelection.Specific("docker"), ForceRemoveOnDispose = true };

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder.UseImage("alpine").WithName(ContainerName);
    }

    [TestMethod]
    public void FirstTest_InitializesSharedContainerOnce()
    {
      Assert.AreEqual("class-container", Container.Id);
      Assert.AreEqual(1, Pack.CreateCalls);
      _firstContainerId = Container.Id;
    }

    [TestMethod]
    public void SecondTest_ReusesSharedContainer()
    {
      Assert.AreEqual("class-container", Container.Id);
      Assert.AreEqual(1, Pack.CreateCalls);
      if (_firstContainerId != null)
        Assert.AreEqual(_firstContainerId, Container.Id);
    }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static async Task Cleanup()
    {
      await CleanupClassAsync().ConfigureAwait(false);
      if (Pack.CreateCalls > 0)
        Assert.IsTrue(Pack.RemoveCalls >= 1, "ClassCleanup did not dispose the shared container");
      RemoveCallsAfterCleanup = Pack.RemoveCalls;
      CleanupCompleted = true;
    }
  }

  [TestClass]
  [TestCategory("Unit")]
  public class ClassContainerFixtureSecondClassRunnerTests
      : MsTestClassContainerFixtureBase<ClassContainerFixtureSecondClassRunnerTests>
  {
    private static readonly MockContainerDriverPack Pack = new();

    protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
        async () => (await MockContainerKernel.CreateAsync(Pack).ConfigureAwait(false)).kernel;

    protected override DockerResourceOptions? GetOptions() =>
        new() { Driver = DriverSelection.Specific("docker"), ForceRemoveOnDispose = true };

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder.UseImage("alpine").WithName($"mstest-class-fixture-second-{Guid.NewGuid():N}");
    }

    [TestMethod]
    public void ClassContainer_IsSharedWithinThisClass()
    {
      if (ClassContainerFixtureRunnerTests.CleanupCompleted)
        Assert.IsTrue(ClassContainerFixtureRunnerTests.RemoveCallsAfterCleanup >= 1);
      Assert.AreEqual("class-container", Container.Id);
      Assert.AreEqual(1, Pack.CreateCalls);
    }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static async Task Cleanup()
    {
      await CleanupClassAsync().ConfigureAwait(false);
    }
  }

  [TestClass]
  [TestCategory("Unit")]
  public class MissingBinaryClassContainerFixtureRunnerTests
      : MsTestClassContainerFixtureBase<MissingBinaryClassContainerFixtureRunnerTests>
  {
    protected override bool SkipWhenUnavailable => true;

    protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
        () => Task.FromException<FluentDockerKernel>(
            new DriverNotAvailableException("docker", "docker binary not found"));

    protected override DockerResourceOptions? GetOptions() =>
        new() { Driver = DriverSelection.Specific("docker"), ForceRemoveOnDispose = true };

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder.UseImage("alpine:latest");
    }

    [TestMethod]
    public void FixtureIsInconclusiveWhenDockerBinaryIsMissing()
    {
      Assert.Fail(
          "MSTest class fixture should be marked inconclusive before the test body runs.");
    }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static async Task Cleanup()
    {
      await CleanupClassAsync().ConfigureAwait(false);
    }
  }

  internal sealed class MockContainerDriverPack : IDriverPack
  {
    private readonly Dictionary<Type, object> _drivers = [];
    private bool _initialized;

    public Mock<IContainerDriver> ContainerDriver { get; } = new();
    public Mock<IImageDriver> ImageDriver { get; } = new();
    public int CreateCalls { get; private set; }
    public int RemoveCalls { get; private set; }

    public MockContainerDriverPack()
    {
      ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(() =>
          {
            CreateCalls++;
            return CommandResponse<ContainerCreateResult>.Ok(
                new ContainerCreateResult { Id = "class-container" });
          });
      ContainerDriver
          .Setup(d => d.StartAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(() =>
          {
            RemoveCalls++;
            return CommandResponse<Unit>.Ok(Unit.Default);
          });
      ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "class-container",
            Name = ClassContainerFixtureRunnerTests.ContainerName,
            State = new ContainerState { Running = true, Status = "running" }
          }));
      ContainerDriver
          .Setup(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
              It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<string>.Ok(""));
    }

    public DriverType Type => DriverType.DockerCli;
    public RuntimeType Runtime => RuntimeType.Docker;

    public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
      _drivers[typeof(IContainerDriver)] = ContainerDriver.Object;
      _drivers[typeof(IImageDriver)] = ImageDriver.Object;
      _initialized = true;
      return Task.CompletedTask;
    }

    public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DriverCapabilities.Default());

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public T SysCtl<T>(string driverId) where T : class
    {
      EnsureInitialized();
      if (_drivers.TryGetValue(typeof(T), out var driver))
        return (T)driver;
      throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
    }

    public object SysCtl(string driverId, Type interfaceType)
    {
      EnsureInitialized();
      if (_drivers.TryGetValue(interfaceType, out var driver))
        return driver;
      throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
    }

    public bool TrySysCtl<T>(string driverId, out T instance) where T : class
    {
      EnsureInitialized();
      if (_drivers.TryGetValue(typeof(T), out var driver))
      {
        instance = (T)driver;
        return true;
      }
      instance = null!;
      return false;
    }

    public bool TryResolve(Type interfaceType, out object implementation)
    {
      EnsureInitialized();
      return _drivers.TryGetValue(interfaceType, out implementation!);
    }

    public IReadOnlyCollection<Type> GetSupportedInterfaces()
    {
      EnsureInitialized();
      return _drivers.Keys.ToList().AsReadOnly();
    }

    private void EnsureInitialized()
    {
      if (!_initialized)
        throw new InvalidOperationException("Mock container pack is not initialized.");
    }
  }

  internal static class MockContainerKernel
  {
    public static async Task<(FluentDockerKernel kernel, MockContainerDriverPack pack)> CreateAsync(
        MockContainerDriverPack pack, string driverId = "docker")
    {
      var context = new DriverContext(driverId);
      await pack.InitializeAsync(context).ConfigureAwait(false);

      var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(driverId, pack, context).ConfigureAwait(false);
      kernel.SetDefaultDriver(driverId);

      return (kernel, pack);
    }
  }
}
