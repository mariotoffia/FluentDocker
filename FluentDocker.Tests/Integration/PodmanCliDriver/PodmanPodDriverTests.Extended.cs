using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Extended Podman pod driver tests: KillPod, PausePod, UnpausePod,
  /// RestartPod, InspectPod.
  /// </summary>
  public partial class PodmanPodDriverTests
  {
    #region KillPodAsync Tests

    [Fact]
    public async Task KillPod_RunningPod_KillsSuccessfully()
    {
      var name = UniqueName("kill-pod");
      try
      {
        await EnsureImageAsync(TestImage);
        await PodDriver.CreatePodAsync(Context,
            new PodCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);

        // Add a container to the pod so it has something to run
        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = ["sleep", "300"],
              Detach = true,
              Pod = name
            }, cancellationToken: TestContext.Current.CancellationToken);

        await PodDriver.StartPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        var killResult = await PodDriver.KillPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(killResult.Success,
            $"Kill failed: {killResult.Error}");

        // Verify pod is no longer running
        var pods = await PodDriver.ListPodsAsync(Context, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(pods.Success);
        var pod = pods.Data.FirstOrDefault(p => p.Name == name);
        Assert.NotNull(pod);
        Assert.NotEqual("Running", pod.Status);
      }
      finally
      {
        await RemovePodAsync(name);
      }
    }

    [Fact]
    public async Task KillPod_WithSignal_Succeeds()
    {
      var name = UniqueName("kill-sig");
      try
      {
        await EnsureImageAsync(TestImage);
        await PodDriver.CreatePodAsync(Context,
            new PodCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);

        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = ["sleep", "300"],
              Detach = true,
              Pod = name
            }, cancellationToken: TestContext.Current.CancellationToken);

        await PodDriver.StartPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        var killResult = await PodDriver.KillPodAsync(
            Context, name, signal: "SIGTERM", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(killResult.Success,
            $"Kill with signal failed: {killResult.Error}");
      }
      finally
      {
        await RemovePodAsync(name);
      }
    }

    [Fact]
    public async Task KillPod_NonExistent_Fails()
    {
      var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
      var result = await PodDriver.KillPodAsync(Context, fakeName, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
    }

    #endregion

    #region PausePod / UnpausePod Tests

    [Fact]
    public async Task PausePod_RunningPod_PausesSuccessfully()
    {
      var name = UniqueName("pause-pod");
      try
      {
        await EnsureImageAsync(TestImage);
        await PodDriver.CreatePodAsync(Context,
            new PodCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);

        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = ["sleep", "300"],
              Detach = true,
              Pod = name
            }, cancellationToken: TestContext.Current.CancellationToken);

        await PodDriver.StartPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        var pauseResult = await PodDriver.PausePodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(pauseResult.Success,
            $"Pause failed: {pauseResult.Error}");

        // Verify pod is paused
        var pods = await PodDriver.ListPodsAsync(Context, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(pods.Success);
        var pod = pods.Data.FirstOrDefault(p => p.Name == name);
        Assert.NotNull(pod);
        Assert.Equal("Paused", pod.Status);
      }
      finally
      {
        // Unpause before remove to avoid errors
        try
        { await PodDriver.UnpausePodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken); }
        catch { }
        await RemovePodAsync(name);
      }
    }

    [Fact]
    public async Task UnpausePod_PausedPod_ResumesRunning()
    {
      var name = UniqueName("unpause-pod");
      try
      {
        await EnsureImageAsync(TestImage);
        await PodDriver.CreatePodAsync(Context,
            new PodCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);

        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = ["sleep", "300"],
              Detach = true,
              Pod = name
            }, cancellationToken: TestContext.Current.CancellationToken);

        await PodDriver.StartPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);
        await PodDriver.PausePodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        var unpauseResult = await PodDriver.UnpausePodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(unpauseResult.Success,
            $"Unpause failed: {unpauseResult.Error}");

        // Verify pod is running again
        var pods = await PodDriver.ListPodsAsync(Context, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(pods.Success);
        var pod = pods.Data.FirstOrDefault(p => p.Name == name);
        Assert.NotNull(pod);
        Assert.Equal("Running", pod.Status);
      }
      finally
      {
        await RemovePodAsync(name);
      }
    }

    #endregion

    #region RestartPodAsync Tests

    [Fact]
    public async Task RestartPod_RunningPod_RestartsSuccessfully()
    {
      var name = UniqueName("restart-pod");
      try
      {
        await EnsureImageAsync(TestImage);
        await PodDriver.CreatePodAsync(Context,
            new PodCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);

        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = ["sleep", "300"],
              Detach = true,
              Pod = name
            }, cancellationToken: TestContext.Current.CancellationToken);

        await PodDriver.StartPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        var restartResult = await PodDriver.RestartPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(restartResult.Success,
            $"Restart failed: {restartResult.Error}");

        // Verify pod is running after restart
        await Task.Delay(2000, cancellationToken: TestContext.Current.CancellationToken);
        var pods = await PodDriver.ListPodsAsync(Context, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(pods.Success);
        var pod = pods.Data.FirstOrDefault(p => p.Name == name);
        Assert.NotNull(pod);
        Assert.Equal("Running", pod.Status);
      }
      finally
      {
        await RemovePodAsync(name);
      }
    }

    [Fact]
    public async Task RestartPod_NonExistent_Fails()
    {
      var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
      var result = await PodDriver.RestartPodAsync(Context, fakeName, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
    }

    #endregion

    #region InspectPodAsync Tests

    [Fact]
    public async Task InspectPod_ExistingPod_ReturnsDetails()
    {
      var name = UniqueName("inspect-pod");
      try
      {
        await PodDriver.CreatePodAsync(Context,
            new PodCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);

        var inspectResult = await PodDriver.InspectPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(inspectResult.Success,
            $"Inspect failed: {inspectResult.Error}");
        Assert.NotNull(inspectResult.Data);
        Assert.Equal(name, inspectResult.Data.Name);
        Assert.NotNull(inspectResult.Data.Id);
      }
      finally
      {
        await RemovePodAsync(name);
      }
    }

    [Fact]
    public async Task InspectPod_WithContainers_ShowsContainerCount()
    {
      var name = UniqueName("inspect-pod");
      try
      {
        await EnsureImageAsync(TestImage);
        await PodDriver.CreatePodAsync(Context,
            new PodCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);

        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = ["sleep", "300"],
              Detach = true,
              Pod = name
            }, cancellationToken: TestContext.Current.CancellationToken);

        var inspectResult = await PodDriver.InspectPodAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(inspectResult.Success);
        // Pod should have at least the infra container + our container
        Assert.True(inspectResult.Data.NumContainers >= 2,
            $"Expected >= 2 containers, got {inspectResult.Data.NumContainers}");
      }
      finally
      {
        await RemovePodAsync(name);
      }
    }

    [Fact]
    public async Task InspectPod_NonExistent_Fails()
    {
      var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
      var result = await PodDriver.InspectPodAsync(Context, fakeName, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
    }

    #endregion
  }
}
