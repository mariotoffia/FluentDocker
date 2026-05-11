using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using ContainerCreateConfig = FluentDocker.Drivers.ContainerCreateConfig;
using ContainerCreateResult = FluentDocker.Drivers.ContainerCreateResult;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.Testing
{
  /// <summary>
  /// Tests for partial provisioning cleanup — verifying that resources created
  /// during a failed build are properly cleaned up to prevent leaks.
  /// </summary>
  [Trait("Category", "Unit")]
  public class BuilderCleanupTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task NonLinkedContainer_InspectFailsDuringWait_ContainerForceRemoved()
    {
      // Create succeeds, Start succeeds, but InspectAsync throws during
      // WaitForContainerRunningAsync — simulates an infrastructure failure
      // after the container is already created and started.
      MockPack
          .SetupContainerCreate("leak-container")
          .SetupContainerStart()
          .SetupContainerRemove();

      // Make inspect throw to trigger failure during WaitForContainerRunningAsync
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("inspect failed"));

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));

      // ContainerBuilder.ExecuteAsync should have force-removed the container
      MockPack.ContainerDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              "leak-container",
              true,
              false,
              It.IsAny<CancellationToken>()),
          Times.AtLeastOnce());
    }

    [Fact]
    public async Task NonLinkedContainer_StartFails_ContainerForceRemoved()
    {
      // Create succeeds but Start throws — container is created but never
      // started. The cleanup should still force-remove it.
      MockPack
          .SetupContainerCreate("start-fail-container")
          .SetupContainerRemove();

      MockPack.ContainerDriver
          .Setup(d => d.StartAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("start failed"));

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));

      // ContainerBuilder.ExecuteAsync should have force-removed the container
      MockPack.ContainerDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              "start-fail-container",
              true,
              false,
              It.IsAny<CancellationToken>()),
          Times.AtLeastOnce());
    }

    [Fact]
    public async Task Topology_SecondContainerCreateFails_FirstContainerCleanedUp()
    {
      // First container create succeeds, second container create throws.
      // Builder.BuildAsync should clean up the first container via
      // scope.DisposeAllAsync().
      var createCallCount = 0;
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .Returns<DriverContext, ContainerCreateConfig, CancellationToken>(
              (_, _, _) =>
              {
                createCallCount++;
                if (createCallCount == 1)
                {
                  return Task.FromResult(
                      CommandResponse<ContainerCreateResult>.Ok(
                          new ContainerCreateResult { Id = "first-container" }));
                }

                throw new InvalidOperationException("second create fails");
              });

      MockPack
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new TopologyResource(
          Kernel,
          builder =>
          {
            builder.UseContainer(c => c.UseImage("redis:alpine"));
            builder.UseContainer(c => c.UseImage("nginx:alpine"));
          },
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));

      // Builder.BuildAsync catch block should have cleaned up the first
      // container by calling scope.DisposeAllAsync() which calls
      // ContainerService.DisposeAsync() → StopAsync + RemoveAsync(force:true)
      MockPack.ContainerDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              "first-container",
              true,
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()),
          Times.AtLeastOnce());
    }

    [Fact]
    public async Task SwarmStack_ProvisioningReturnsNullData_TeardownStillRunsOnDispose()
    {
      // Deploy returns Success but with null Data — ProvisionAsync throws
      // "returned Success but no result payload". Because _provisioned is
      // now set before ProvisionAsync, DisposeAsync will call TeardownAsync
      // which no longer guards on DeployResult == null.
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = true
      });
      MockPack.EnableStackDriver();
      MockPack.SetupStackRemove();

      MockPack.StackDriver
          .Setup(d => d.DeployAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<StackDeployConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<StackDeployResult>.Ok(null));

      var resource = new SwarmStackResource(
          Kernel,
          new StackDeployConfig { StackName = "partial-stack" },
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await Assert.ThrowsAsync<FluentDockerException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));

      // Dispose should attempt cleanup via TeardownAsync
      await resource.DisposeAsync();

      MockPack.VerifyStackRemoved(Times.AtLeastOnce());
    }

    [Fact]
    public async Task PodmanKube_ProvisioningReturnsNullData_TeardownStillRunsOnDispose()
    {
      // Play returns Success but with null Data — ProvisionAsync throws.
      // Same pattern as SwarmStack test above.
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsKubernetes = true
      });
      MockPack.EnablePodmanKubernetesDriver();
      MockPack.SetupKubeDown();

      MockPack.PodmanKubernetesDriver
          .Setup(d => d.PlayAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<KubePlayConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<KubePlayResult>.Ok(null));

      var resource = new PodmanKubernetesResource(
          Kernel,
          new KubePlayConfig { YamlPath = "partial.yaml" },
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await Assert.ThrowsAsync<FluentDockerException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));

      // Dispose should attempt cleanup via TeardownAsync
      await resource.DisposeAsync();

      MockPack.VerifyKubeDown(Times.AtLeastOnce());
    }

    [Fact]
    public async Task DisposeAsync_ProvisionNeverRan_SkipsTeardown()
    {
      // Verify that _provisioned=true before ProvisionAsync doesn't
      // break the case where PreflightAsync fails (no provisioning at all).
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = false
      });

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await Assert.ThrowsAsync<CapabilityNotSupportedException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));

      // Dispose should not call any container driver methods
      // because preflight failed (before _provisioned was set)
      await resource.DisposeAsync();

      MockPack.ContainerDriver.Verify(
          d => d.StopAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<int?>(),
              It.IsAny<CancellationToken>()),
          Times.Never());

      MockPack.ContainerDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()),
          Times.Never());
    }
  }
}
