using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Integration tests for Podman container driver.
  /// Requires Podman to be installed.
  /// </summary>
  [Collection("PodmanDriver")]
  [Trait("Category", "PodmanIntegration")]
  public partial class PodmanContainerDriverTests : PodmanDriverTestBase
  {
    [Fact]
    public async Task CreateAndRemove_Succeeds()
    {
      await EnsureImageAsync(TestImage);
      string? containerId = null;

      try
      {
        var config = new ContainerCreateConfig
        {
          Image = TestImage,
          Name = UniqueName("create-test"),
          Command = ["sleep", "60"]
        };

        var createResult = await ContainerDriver.CreateAsync(Context, config, TestContext.Current.CancellationToken);
        Assert.True(createResult.Success, $"Create failed: {createResult.Error}");
        containerId = createResult.Data.Id;
        Assert.NotNull(containerId);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task RunDetached_Succeeds()
    {
      await EnsureImageAsync(TestImage);
      string? containerId = null;

      try
      {
        var config = new ContainerCreateConfig
        {
          Image = TestImage,
          Detach = true,
          Command = ["sleep", "60"]
        };

        var runResult = await ContainerDriver.RunAsync(Context, config, TestContext.Current.CancellationToken);
        Assert.True(runResult.Success, $"Run failed: {runResult.Error}");
        containerId = runResult.Data.Id;
        Assert.NotNull(containerId);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task StartStopRestart_Lifecycle()
    {
      await EnsureImageAsync(TestImage);
      string? containerId = null;

      try
      {
        var config = new ContainerCreateConfig
        {
          Image = TestImage,
          Command = ["sleep", "60"]
        };

        var createResult = await ContainerDriver.CreateAsync(Context, config, TestContext.Current.CancellationToken);
        Assert.True(createResult.Success);
        containerId = createResult.Data.Id;

        // Start
        var startResult = await ContainerDriver.StartAsync(Context, containerId, TestContext.Current.CancellationToken);
        Assert.True(startResult.Success, $"Start failed: {startResult.Error}");

        // Stop
        var stopResult = await ContainerDriver.StopAsync(Context, containerId, timeout: 5, TestContext.Current.CancellationToken);
        Assert.True(stopResult.Success, $"Stop failed: {stopResult.Error}");

        // Restart
        var restartResult = await ContainerDriver.RestartAsync(Context, containerId, timeout: 5, TestContext.Current.CancellationToken);
        Assert.True(restartResult.Success, $"Restart failed: {restartResult.Error}");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Inspect_ReturnsContainerDetails()
    {
      await EnsureImageAsync(TestImage);
      string? containerId = null;

      try
      {
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = ["sleep", "60"]
        });

        var result = await ContainerDriver.InspectAsync(Context, containerId, TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"Inspect failed: {result.Error}");
        Assert.NotNull(result.Data.Id);
        Assert.NotNull(result.Data.State);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task ListContainers_ReturnsResults()
    {
      await EnsureImageAsync(TestImage);
      string? containerId = null;

      try
      {
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = ["sleep", "60"]
        });

        var result = await ContainerDriver.ListAsync(Context,
            new ContainerListFilter { All = true }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"List failed: {result.Error}");
        Assert.NotEmpty(result.Data);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }
  }
}
