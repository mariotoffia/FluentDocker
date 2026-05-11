using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Integration tests for Podman pod driver.
  /// Requires Podman to be installed.
  /// </summary>
  [Collection("PodmanDriver")]
  [Trait("Category", "PodmanIntegration")]
  public partial class PodmanPodDriverTests : PodmanDriverTestBase
  {
    [Fact]
    public async Task CreateAndRemove_Succeeds()
    {
      var name = UniqueName("pod");
      try
      {
        var config = new PodCreateConfig { Name = name };
        var createResult = await PodDriver.CreatePodAsync(Context, config, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(createResult.Success, $"Create failed: {createResult.Error}");
        Assert.NotNull(createResult.Data.Id);
      }
      finally
      {
        await RemovePodAsync(name);
      }
    }

    [Fact]
    public async Task ListPods_ReturnsResults()
    {
      var name = UniqueName("pod");
      try
      {
        await PodDriver.CreatePodAsync(Context, new PodCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);

        var result = await PodDriver.ListPodsAsync(Context, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"List failed: {result.Error}");
        Assert.Contains(result.Data, p => p.Name == name);
      }
      finally
      {
        await RemovePodAsync(name);
      }
    }

    [Fact]
    public async Task InspectPod_ReturnsDetails()
    {
      var name = UniqueName("pod");
      try
      {
        await PodDriver.CreatePodAsync(Context, new PodCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);

        var result = await PodDriver.InspectPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"Inspect failed: {result.Error}");
        Assert.Equal(name, result.Data.Name);
        Assert.NotNull(result.Data.Id);
      }
      finally
      {
        await RemovePodAsync(name);
      }
    }

    [Fact]
    public async Task StartStopRestart_Lifecycle()
    {
      var name = UniqueName("pod");
      try
      {
        await PodDriver.CreatePodAsync(Context, new PodCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);

        var startResult = await PodDriver.StartPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(startResult.Success, $"Start failed: {startResult.Error}");

        var stopResult = await PodDriver.StopPodAsync(Context, name, timeout: 5, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(stopResult.Success, $"Stop failed: {stopResult.Error}");

        var restartResult = await PodDriver.RestartPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(restartResult.Success, $"Restart failed: {restartResult.Error}");
      }
      finally
      {
        await RemovePodAsync(name);
      }
    }

    [Fact]
    public async Task PauseUnpause_Succeeds()
    {
      var name = UniqueName("pod");
      try
      {
        await PodDriver.CreatePodAsync(Context, new PodCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);
        await PodDriver.StartPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        var pauseResult = await PodDriver.PausePodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(pauseResult.Success, $"Pause failed: {pauseResult.Error}");

        var unpauseResult = await PodDriver.UnpausePodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(unpauseResult.Success, $"Unpause failed: {unpauseResult.Error}");
      }
      finally
      {
        await RemovePodAsync(name);
      }
    }

    [Fact]
    public async Task CreateContainerInPod_Succeeds()
    {
      await EnsureImageAsync(TestImage);
      var podName = UniqueName("pod");

      try
      {
        await PodDriver.CreatePodAsync(Context, new PodCreateConfig { Name = podName }, cancellationToken: TestContext.Current.CancellationToken);

        var config = new ContainerCreateConfig
        {
          Image = TestImage,
          Command = ["sleep", "60"],
          Pod = podName
        };

        var createResult = await ContainerDriver.CreateAsync(Context, config, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(createResult.Success, $"Container create failed: {createResult.Error}");

        _ = createResult.Data.Id;


        // Verify the pod now shows containers
        var inspectResult = await PodDriver.InspectPodAsync(Context, podName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspectResult.Success);
        Assert.True(inspectResult.Data.NumContainers > 0 ||
                    inspectResult.Data.Containers.Count > 0);
      }
      finally
      {
        await RemovePodAsync(podName);
      }
    }
  }
}
