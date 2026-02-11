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
            new PodCreateConfig { Name = name });

        // Add a container to the pod so it has something to run
        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = new[] { "sleep", "300" },
              Detach = true,
              Pod = name
            });

        await PodDriver.StartPodAsync(Context, name);

        var killResult = await PodDriver.KillPodAsync(Context, name);

        Assert.True(killResult.Success,
            $"Kill failed: {killResult.Error}");

        // Verify pod is no longer running
        var pods = await PodDriver.ListPodsAsync(Context);
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
            new PodCreateConfig { Name = name });

        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = new[] { "sleep", "300" },
              Detach = true,
              Pod = name
            });

        await PodDriver.StartPodAsync(Context, name);

        var killResult = await PodDriver.KillPodAsync(
            Context, name, signal: "SIGTERM");

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
      var result = await PodDriver.KillPodAsync(Context, fakeName);
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
            new PodCreateConfig { Name = name });

        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = new[] { "sleep", "300" },
              Detach = true,
              Pod = name
            });

        await PodDriver.StartPodAsync(Context, name);

        var pauseResult = await PodDriver.PausePodAsync(Context, name);

        Assert.True(pauseResult.Success,
            $"Pause failed: {pauseResult.Error}");

        // Verify pod is paused
        var pods = await PodDriver.ListPodsAsync(Context);
        Assert.True(pods.Success);
        var pod = pods.Data.FirstOrDefault(p => p.Name == name);
        Assert.NotNull(pod);
        Assert.Equal("Paused", pod.Status);
      }
      finally
      {
        // Unpause before remove to avoid errors
        try { await PodDriver.UnpausePodAsync(Context, name); }
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
            new PodCreateConfig { Name = name });

        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = new[] { "sleep", "300" },
              Detach = true,
              Pod = name
            });

        await PodDriver.StartPodAsync(Context, name);
        await PodDriver.PausePodAsync(Context, name);

        var unpauseResult = await PodDriver.UnpausePodAsync(Context, name);

        Assert.True(unpauseResult.Success,
            $"Unpause failed: {unpauseResult.Error}");

        // Verify pod is running again
        var pods = await PodDriver.ListPodsAsync(Context);
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
            new PodCreateConfig { Name = name });

        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = new[] { "sleep", "300" },
              Detach = true,
              Pod = name
            });

        await PodDriver.StartPodAsync(Context, name);

        var restartResult = await PodDriver.RestartPodAsync(Context, name);

        Assert.True(restartResult.Success,
            $"Restart failed: {restartResult.Error}");

        // Verify pod is running after restart
        await Task.Delay(2000);
        var pods = await PodDriver.ListPodsAsync(Context);
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
      var result = await PodDriver.RestartPodAsync(Context, fakeName);
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
            new PodCreateConfig { Name = name });

        var inspectResult = await PodDriver.InspectPodAsync(Context, name);

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
            new PodCreateConfig { Name = name });

        await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = new[] { "sleep", "300" },
              Detach = true,
              Pod = name
            });

        var inspectResult = await PodDriver.InspectPodAsync(Context, name);

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
      var result = await PodDriver.InspectPodAsync(Context, fakeName);
      Assert.False(result.Success);
    }

    #endregion
  }
}
