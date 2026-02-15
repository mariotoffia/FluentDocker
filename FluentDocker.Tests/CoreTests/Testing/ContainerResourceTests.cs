using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ContainerResourceTests : MockKernelTestBase, IAsyncLifetime
  {
    public async Task InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    public Task DisposeAsync()
    {
      return base.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task InitializeAsync_CreatesAndStartsContainer()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest").WithName("test"));

      await resource.InitializeAsync();

      Assert.True(resource.IsInitialized);
      Assert.NotNull(resource.Container);
    }

    [Fact]
    public async Task DisposeAsync_StopsAndRemovesContainer()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"));

      await resource.InitializeAsync();
      await resource.DisposeAsync();

      Assert.False(resource.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_IsIdempotent()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"));

      await resource.InitializeAsync();
      await resource.InitializeAsync(); // second call is no-op

      Assert.True(resource.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_FailsPreflightWhenContainersNotSupported()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = false
      });

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"));

      await Assert.ThrowsAsync<FluentDocker.Common.CapabilityNotSupportedException>(
          () => resource.InitializeAsync());
    }

    [Fact]
    public void Constructor_NullKernel_Throws()
    {
      Assert.Throws<ArgumentNullException>(
          () => new ContainerResource(null, _ => { }));
    }

    [Fact]
    public void Constructor_NullConfigure_Throws()
    {
      Assert.Throws<ArgumentNullException>(
          () => new ContainerResource(Kernel, null));
    }

    [Fact]
    public async Task GetLogsAsync_BeforeInit_Throws()
    {
      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"));

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.GetLogsAsync());
    }

    [Fact]
    public async Task ExecuteAsync_BeforeInit_Throws()
    {
      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"));

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.ExecuteAsync("echo hello"));
    }

    [Fact]
    public async Task LifecycleHooks_AreCalledInOrder()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var order = new List<string>();

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"));

      resource
          .OnBeforeInitialize(_ => { order.Add("beforeInit"); return Task.CompletedTask; })
          .OnAfterReady(_ => { order.Add("afterReady"); return Task.CompletedTask; })
          .OnBeforeDispose(_ => { order.Add("beforeDispose"); return Task.CompletedTask; })
          .OnAfterDispose(_ => { order.Add("afterDispose"); return Task.CompletedTask; });

      await resource.InitializeAsync();
      await resource.DisposeAsync();

      Assert.Equal(new[] { "beforeInit", "afterReady", "beforeDispose", "afterDispose" }, order);
    }

    [Fact]
    public void Options_DefaultValues_AreReasonable()
    {
      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"));

      Assert.True(resource.Options.ForceRemoveOnDispose);
      Assert.True(resource.Options.CaptureLogsOnFailure);
      Assert.Equal(TimeSpan.FromMinutes(2), resource.Options.InitializationTimeout);
      Assert.Equal(200, resource.Options.MaxDiagnosticLogLines);
    }

    [Fact]
    public async Task DriverSelection_Default_UsesKernelDefault()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"));

      await resource.InitializeAsync();

      Assert.Equal(DriverId, resource.DriverId);
    }

    [Fact]
    public async Task DriverSelection_Specific_UsesProvidedId()
    {
      var secondPack = new MockDriverPack();
      var context = new DriverContext("custom-docker");
      await secondPack.InitializeAsync(context);
      await Kernel.RegisterDriverPackAsync("custom-docker", secondPack, context);

      secondPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions
          {
            Driver = DriverSelection.Specific("custom-docker")
          });

      await resource.InitializeAsync();

      Assert.Equal("custom-docker", resource.DriverId);
    }
  }
}
