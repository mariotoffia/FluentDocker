using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public sealed class BuilderCapabilityRemediationTests
  {
    [Fact]
    public async Task UseContainer_WithoutForcePull_DoesNotRequireImageDriver()
    {
      var pack = new ContainerOnlyPack()
          .SetupCreate()
          .SetupStart()
          .SetupInspect(running: true)
          .SetupRemove();
      var kernel = await CreateKernelAsync(pack);
      await using (kernel)
      {
        await using var results = await new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("alpine"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(results.All);
      }
    }

    [Fact]
    public async Task ForcePullImage_WithoutImageDriver_ThrowsClearException()
    {
      var pack = new ContainerOnlyPack()
          .SetupCreate()
          .SetupStart()
          .SetupInspect(running: true)
          .SetupRemove();
      var kernel = await CreateKernelAsync(pack);
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("alpine").ForcePullImage())
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("ForcePullImage", ex.Message);
        Assert.Contains("IImageDriver", ex.Message);
      }
    }

    private static async Task<FluentDockerKernel> CreateKernelAsync(IDriverPack pack)
    {
      var context = new DriverContext("docker");
      await pack.InitializeAsync(context, TestContext.Current.CancellationToken);
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync("docker", pack, context, TestContext.Current.CancellationToken);
      kernel.SetDefaultDriver("docker");
      return kernel;
    }

    private sealed class ContainerOnlyPack : IDriverPack
    {
      private readonly Dictionary<Type, object> _drivers = [];
      private bool _initialized;
      internal Mock<IContainerDriver> ContainerDriver { get; } = new();
      public DriverType Type => DriverType.DockerCli;
      public RuntimeType Runtime => RuntimeType.Docker;
      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult(DriverCapabilities.Default());
      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default)
      {
        _drivers[typeof(IContainerDriver)] = ContainerDriver.Object;
        _initialized = true;
        return Task.CompletedTask;
      }

      public T SysCtl<T>(string driverId) where T : class => TryResolve(typeof(T), out var driver)
          ? (T)driver
          : throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
      public object SysCtl(string driverId, Type interfaceType) => TryResolve(interfaceType, out var driver)
          ? driver
          : throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
      public bool TrySysCtl<T>(string driverId, [NotNullWhen(true)] out T? instance) where T : class
      {
        if (TryResolve(typeof(T), out var driver))
        {
          instance = (T)driver;
          return true;
        }
        instance = null;
        return false;
      }
      public bool TryResolve(Type interfaceType, out object implementation) => _drivers.TryGetValue(interfaceType, out implementation!);
      public IReadOnlyCollection<Type> GetSupportedInterfaces() => _initialized ? _drivers.Keys.ToList().AsReadOnly() : [];

      internal ContainerOnlyPack SetupCreate(string id = "container-1")
      {
        ContainerDriver.Setup(d => d.CreateAsync(It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult { Id = id }));
        return this;
      }

      internal ContainerOnlyPack SetupStart()
      {
        ContainerDriver.Setup(d => d.StartAsync(It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
        return this;
      }

      internal ContainerOnlyPack SetupInspect(bool running)
      {
        ContainerDriver.Setup(d => d.InspectAsync(It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommandResponse<Container>.Ok(new Container { Id = "container-1", Name = "container-1", State = new ContainerState { Running = running, Status = running ? "running" : "exited" } }));
        return this;
      }

      internal ContainerOnlyPack SetupRemove()
      {
        ContainerDriver.Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
        return this;
      }
    }
  }
}
