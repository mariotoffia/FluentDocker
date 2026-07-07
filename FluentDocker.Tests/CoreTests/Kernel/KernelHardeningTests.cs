using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Kernel;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  [Trait("Category", "Unit")]
  public class KernelHardeningTests
  {
    [Fact]
    public async Task SysCtl_WithNullDriverId_UsesConfiguredDefault()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var pack = new ResolvingPack();
      await kernel.RegisterDriverPackAsync(
          "default", pack, new DriverContext("default"), TestContext.Current.CancellationToken);

      var resolved = kernel.SysCtl<IImageDriver>(null!);

      Assert.Same(pack.ImageDriver.Object, resolved);
    }

    [Fact]
    public void SysCtl_WithNullDriverIdAndNoDefault_ThrowsClearDefaultDriverError()
    {
      var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

      var ex = Assert.Throws<InvalidOperationException>(() =>
          kernel.SysCtl(null!, typeof(IImageDriver)));

      Assert.Contains("No default driver configured", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SysCtl_WhenGenericInterfaceUnsupported_UsesFriendlyInterfaceName()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "driver", new EmptyPack(), new DriverContext("driver"), TestContext.Current.CancellationToken);

      var ex = Assert.Throws<InterfaceNotSupportedException>(() =>
          kernel.SysCtl<IGenericMissing<string>>("driver"));

      Assert.Contains("<", ex.Message, StringComparison.Ordinal);
      Assert.DoesNotContain("`", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScope_ExposesKernelAsSysCtlAbstraction()
    {
      var property = typeof(BuildScope).GetProperty(nameof(BuildScope.Kernel));

      Assert.NotNull(property);
      Assert.Equal(typeof(ISysCtl), property.PropertyType);
    }

    [Fact]
    public async Task BuiltInPackCapabilityFlags_MatchSupportedInterfaces()
    {
      var packs = new IDriverPack[]
      {
        new DockerCliDriverPack(),
        new DockerApiDriverPack(),
        new PodmanCliDriverPack()
      };

      foreach (var pack in packs)
      {
        await pack.InitializeAsync(
            new DriverContext("driver") { ModelRunnerEndpoint = ModelRunnerEndpoint.HostTcp() },
            TestContext.Current.CancellationToken);

        var capabilities = await pack.GetCapabilitiesAsync(TestContext.Current.CancellationToken);
        var supported = pack.GetSupportedInterfaces().ToHashSet();

        Assert.Equal(supported.Contains(typeof(IContainerDriver)), capabilities.SupportsContainers);
        Assert.Equal(supported.Contains(typeof(IImageDriver)), capabilities.SupportsImages);
        Assert.Equal(supported.Contains(typeof(INetworkDriver)), capabilities.SupportsNetworks);
        Assert.Equal(supported.Contains(typeof(IVolumeDriver)), capabilities.SupportsVolumes);
        Assert.Equal(supported.Contains(typeof(IComposeDriver)), capabilities.SupportsCompose);
        Assert.Equal(supported.Contains(typeof(ISystemDriver)), capabilities.SupportsSystem);
        Assert.Equal(supported.Contains(typeof(IStackDriver)), capabilities.SupportsStacks);
        Assert.Equal(supported.Contains(typeof(IServiceDriver)), capabilities.SupportsServices);
        Assert.Equal(
            supported.Contains(typeof(IModelManagementDriver)) ||
            supported.Contains(typeof(IModelRuntimeDriver)) ||
            supported.Contains(typeof(IModelInferenceDriver)),
            capabilities.SupportsModels);

        if (pack is IAsyncDisposable asyncDisposable)
          await asyncDisposable.DisposeAsync();
      }
    }

    [Fact]
    public void DriverContextCloneWith_IsolatesMutableState()
    {
      var original = new DriverContext("original")
      {
        Metadata = new Dictionary<string, string> { ["key"] = "value" },
        SearchPaths = ["/first"],
        AutoStartMachine = new AutoStartMachineConfig
        {
          MachineName = "machine",
          CreateIfNotExists = true,
          InitCpus = 2
        }
      };

      var clone = original.CloneWith("clone", NullLoggerFactory.Instance);
      clone.Metadata["key"] = "changed";
      clone.SearchPaths[0] = "/changed";
      clone.AutoStartMachine.MachineName = "changed";

      Assert.Equal("value", original.Metadata["key"]);
      Assert.Equal("/first", original.SearchPaths[0]);
      Assert.Equal("machine", original.AutoStartMachine.MachineName);
    }

    [Fact]
    public void DockerCliPerCallContext_UsesRegisteredDefaultsWhenOperationOmitsThem()
    {
      var driver = new InspectableDockerCliDriver();
      driver.Initialize(new DriverContext("registered")
      {
        DefaultShell = "zsh",
        Metadata = new Dictionary<string, string> { ["registered"] = "yes" }
      });

      var effective = driver.Merge(new DriverContext());

      Assert.Equal("zsh", effective.DefaultShell);
      Assert.Contains("registered", effective.Metadata.Keys);
    }

    [Fact]
    public async Task DockerCliPack_SysCtlVsDisposeAsync_DoesNotReturnAfterDisposed()
    {
      var pack = new DockerCliDriverPack();
      await pack.InitializeAsync(
          new DriverContext("docker") { ModelRunnerEndpoint = ModelRunnerEndpoint.HostTcp() },
          TestContext.Current.CancellationToken);

      var dispose = pack.DisposeAsync().AsTask();
      await dispose.WaitAsync(TestContext.Current.CancellationToken);

      Assert.Throws<ObjectDisposedException>(() =>
          pack.TrySysCtl<IModelInferenceDriver>("docker", out _));
    }

    [Fact]
    public async Task DriverRegistryDefault_AfterUnregisterUsesFirstRegisteredStillPresent()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      await registry.RegisterDriverPackAsync(
          "first", new EmptyPack(), new DriverContext("first"), TestContext.Current.CancellationToken);
      await registry.RegisterDriverPackAsync(
          "second", new EmptyPack(), new DriverContext("second"), TestContext.Current.CancellationToken);

      await registry.UnregisterAsync("first", TestContext.Current.CancellationToken);
      await registry.RegisterDriverPackAsync(
          "first", new EmptyPack(), new DriverContext("first"), TestContext.Current.CancellationToken);

      Assert.Equal("second", registry.GetDefaultDriverId());
    }

    [Fact]
    public void DriverRegistryDispose_WhenLockTimeouts_DoesNotThrow()
    {
      using var registry = new LockTimeoutRegistry();
      registry.HoldRegistrationLock();

      registry.Dispose();
    }

    [Fact]
    public void RegistryLoginConfig_RedactsPasswordFromJsonAndToStringBytes()
    {
      var config = new RegistryLoginConfig
      {
        Server = "registry.example",
        Username = "user",
        Password = "secret-value"
      };

      var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config));
      var stringBytes = Encoding.UTF8.GetBytes(config.ToString());
      var secretBytes = Encoding.UTF8.GetBytes("secret-value");

      Assert.DoesNotContain(secretBytes, jsonBytes);
      Assert.DoesNotContain(secretBytes, stringBytes);
      Assert.Contains(Encoding.UTF8.GetBytes("***"), stringBytes);
    }

    private sealed class InspectableDockerCliDriver : DockerCliDriverBase
    {
      public DriverContext Merge(DriverContext context)
      {
        var method = typeof(DockerCliDriverBase).GetMethod(
            "CreateEffectiveContext", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (DriverContext)method.Invoke(this, [context])!;
      }
    }

    private sealed class ResolvingPack : EmptyPack
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

    private class EmptyPack : IDriverPack
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
        return false;
      }

      public IReadOnlyCollection<Type> GetSupportedInterfaces() => [];
    }

    private sealed class LockTimeoutRegistry : DriverRegistry
    {
      private readonly FieldInfo _lockField = typeof(DriverRegistry).GetField(
          "_registrationLock", BindingFlags.Instance | BindingFlags.NonPublic)!;

      public LockTimeoutRegistry() : base(NullLoggerFactory.Instance)
      {
      }

      protected override TimeSpan DisposeBudget => TimeSpan.FromMilliseconds(1);

      public void HoldRegistrationLock()
      {
        var semaphore = (SemaphoreSlim)_lockField.GetValue(this)!;
        semaphore.Wait();
      }

    }

    private interface IGenericMissing<T>
    {
    }
  }
}
