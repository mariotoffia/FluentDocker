using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class SwarmStackResourceTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task InitializeAndDispose_Lifecycle_Succeeds()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = true
      });
      MockPack.EnableStackDriver();
      MockPack.SetupStackDeploy("my-stack");
      MockPack.SetupStackRemove();

      var config = new StackDeployConfig
      {
        StackName = "my-stack",
        ComposeFiles = { "docker-compose.yml" }
      };

      var resource = new SwarmStackResource(Kernel, config);

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.True(resource.IsInitialized);
      Assert.Equal("my-stack", resource.StackName);
      Assert.NotNull(resource.DeployResult);

      await resource.DisposeAsync();
      Assert.False(resource.IsInitialized);
    }

    [Fact]
    public async Task PreflightAsync_FailsWithoutSupportsStacks()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = false
      });

      var config = new StackDeployConfig { StackName = "test" };
      var resource = new SwarmStackResource(Kernel, config);

      await Assert.ThrowsAsync<CapabilityNotSupportedException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PreflightAsync_FailsWithoutIStackDriver()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = true
      });
      // Don't call EnableStackDriver() — IStackDriver not registered

      var config = new StackDeployConfig { StackName = "test" };
      var resource = new SwarmStackResource(Kernel, config);

      await Assert.ThrowsAsync<InterfaceNotSupportedException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deploy_Failure_ThrowsFluentDockerException()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = true
      });
      MockPack.EnableStackDriver();

      // Setup DeployAsync to return failure
      MockPack.StackDriver
          .Setup(d => d.DeployAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<StackDeployConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<StackDeployResult>.Fail("deploy failed"));

      var config = new StackDeployConfig { StackName = "fail-stack" };
      var resource = new SwarmStackResource(Kernel, config);

      var ex = await Assert.ThrowsAsync<FluentDockerException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));
      Assert.Contains("fail-stack", ex.Message);
    }

    [Fact]
    public async Task ListServicesAsync_DelegatesToDriver()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = true
      });
      MockPack.EnableStackDriver();
      MockPack.SetupStackDeploy("svc-stack");
      MockPack.SetupStackRemove();
      MockPack.SetupStackGetServices(
          new StackServiceInfo { Id = "svc1", Name = "web" });

      var config = new StackDeployConfig { StackName = "svc-stack" };
      var resource = new SwarmStackResource(Kernel, config);
      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      var services = await resource.ListServicesAsync(TestContext.Current.CancellationToken);

      Assert.Single(services);
      Assert.Equal("web", services[0].Name);

      await resource.DisposeAsync();
    }

    [Fact]
    public async Task ListTasksAsync_DelegatesToDriver()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = true
      });
      MockPack.EnableStackDriver();
      MockPack.SetupStackDeploy("task-stack");
      MockPack.SetupStackRemove();
      MockPack.SetupStackGetTasks(
          new StackTask { Id = "t1", Name = "task1", CurrentState = "running" });

      var config = new StackDeployConfig { StackName = "task-stack" };
      var resource = new SwarmStackResource(Kernel, config);
      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      var tasks = await resource.ListTasksAsync(TestContext.Current.CancellationToken);

      Assert.Single(tasks);
      Assert.Equal("running", tasks[0].CurrentState);

      await resource.DisposeAsync();
    }

    [Fact]
    public async Task AccessBeforeInit_ThrowsInvalidOperationException()
    {
      var config = new StackDeployConfig { StackName = "noinit" };
      var resource = new SwarmStackResource(Kernel, config);

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.ListServicesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListServicesAsync_Failure_ThrowsFluentDockerException()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = true
      });
      MockPack.EnableStackDriver();
      MockPack.SetupStackDeploy("fail-svc");
      MockPack.SetupStackRemove();

      MockPack.StackDriver
          .Setup(d => d.GetServicesAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<StackServiceFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<System.Collections.Generic.IList<StackServiceInfo>>.Fail("service list error"));

      var config = new StackDeployConfig { StackName = "fail-svc" };
      var resource = new SwarmStackResource(Kernel, config);
      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      var ex = await Assert.ThrowsAsync<FluentDockerException>(
          () => resource.ListServicesAsync(TestContext.Current.CancellationToken));
      Assert.Contains("service list error", ex.Message);

      await resource.DisposeAsync();
    }

    [Fact]
    public async Task ListTasksAsync_Failure_ThrowsFluentDockerException()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = true
      });
      MockPack.EnableStackDriver();
      MockPack.SetupStackDeploy("fail-task");
      MockPack.SetupStackRemove();

      MockPack.StackDriver
          .Setup(d => d.GetTasksAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<StackTaskFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<System.Collections.Generic.IList<StackTask>>.Fail("task list error"));

      var config = new StackDeployConfig { StackName = "fail-task" };
      var resource = new SwarmStackResource(Kernel, config);
      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      var ex = await Assert.ThrowsAsync<FluentDockerException>(
          () => resource.ListTasksAsync(TestContext.Current.CancellationToken));
      Assert.Contains("task list error", ex.Message);

      await resource.DisposeAsync();
    }

    [Fact]
    public async Task TeardownAsync_RemoveFailure_ForceRemoveHandlesIt()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = true
      });
      MockPack.EnableStackDriver();
      MockPack.SetupStackDeploy("rm-fail");

      // RemoveAsync returns failure — triggers ForceRemoveAsync path
      MockPack.StackDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string[]>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("remove failed"));

      var config = new StackDeployConfig { StackName = "rm-fail" };
      var resource = new SwarmStackResource(Kernel, config,
          new DockerResourceOptions { ForceRemoveOnDispose = true });
      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      // DisposeAsync should not throw — ForceRemoveAsync is best-effort
      await resource.DisposeAsync();
      Assert.False(resource.IsInitialized);

      // RemoveAsync called twice: once from Teardown, once from ForceRemove
      MockPack.VerifyStackRemoved(Times.Exactly(2));
    }

    [Fact]
    public void Constructor_NullKernel_Throws()
    {
      Assert.Throws<ArgumentNullException>(
          () => new SwarmStackResource(null, new StackDeployConfig { StackName = "x" }));
    }

    [Fact]
    public void Constructor_NullConfig_Throws()
    {
      Assert.Throws<ArgumentNullException>(
          () => new SwarmStackResource(Kernel, null));
    }

    [Fact]
    public void Constructor_EmptyStackName_ThrowsArgumentException()
    {
      Assert.Throws<ArgumentException>(
          () => new SwarmStackResource(Kernel, new StackDeployConfig { StackName = "" }));
    }

    [Fact]
    public void Constructor_NullStackName_ThrowsArgumentException()
    {
      Assert.Throws<ArgumentException>(
          () => new SwarmStackResource(Kernel, new StackDeployConfig()));
    }

    [Fact]
    public async Task DisposeAsync_BeforeInit_DoesNotThrow()
    {
      var config = new StackDeployConfig { StackName = "never-init" };
      var resource = new SwarmStackResource(Kernel, config);

      // Should not throw — teardown guards against uninitialized state
      await resource.DisposeAsync();
      Assert.False(resource.IsInitialized);
    }
  }
}
