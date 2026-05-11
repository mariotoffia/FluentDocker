using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>Extended compose driver tests for untested IComposeDriver methods.</summary>
  public partial class ComposeDriverTests
  {
    #region BuildAsync Tests
    [Fact]
    public async Task Build_HelloTest_BuildsImage()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("hellotest/docker-compose.yml");

      try
      {
        var buildResult = await ComposeDriver.BuildAsync(Context, new ComposeBuildConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          NoCache = true
        }, TestContext.Current.CancellationToken);

        Assert.True(buildResult.Success, $"Build failed: {buildResult.Error}");
      }
      finally
      {
        // Clean up built images
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          RemoveImages = "local"
        }, TestContext.Current.CancellationToken);
      }
    }

    [Fact]
    public async Task Build_NonExistentFile_Fails()
    {
      var result = await ComposeDriver.BuildAsync(Context, new ComposeBuildConfig
      {
        ComposeFiles = ["/nonexistent/docker-compose.yml"],
        ProjectName = "fail-test"
      }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
    }

    #endregion

    #region CreateAsync Tests
    [Fact]
    public async Task Create_WithoutStarting_CreatesButDoesNotRun()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");

      try
      {
        var createResult = await ComposeDriver.CreateAsync(Context, new ComposeCreateConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName
        }, TestContext.Current.CancellationToken);
        Assert.True(createResult.Success, $"Create failed: {createResult.Error}");

        // Containers should exist but NOT be running
        var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          All = true
        }, TestContext.Current.CancellationToken);
        Assert.True(listResult.Success);
        Assert.True(listResult.Data.Count >= 1, "Should have created containers");
        Assert.DoesNotContain(listResult.Data,
            s => s.State?.ToLower() == "running");
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
      }
    }

    [Fact]
    public async Task Create_ThenStart_ContainersRun()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");

      try
      {
        await ComposeDriver.CreateAsync(Context, new ComposeCreateConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName
        }, TestContext.Current.CancellationToken);

        var startResult = await ComposeDriver.StartAsync(Context, new ComposeFileConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName
        }, TestContext.Current.CancellationToken);
        Assert.True(startResult.Success, $"Start failed: {startResult.Error}");

        await Task.Delay(3000, TestContext.Current.CancellationToken);

        var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName
        }, TestContext.Current.CancellationToken);
        Assert.True(listResult.Success);
        Assert.Contains(listResult.Data,
            s => s.State?.ToLower() == "running");
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
      }
    }

    #endregion

    #region RestartAsync Tests
    [Fact]
    public async Task Restart_RunningService_RestartsSuccessfully()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");

      try
      {
        await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        }, TestContext.Current.CancellationToken);
        await Task.Delay(5000, TestContext.Current.CancellationToken);

        var restartResult = await ComposeDriver.RestartAsync(Context,
            new ComposeRestartConfig
            {
              ComposeFiles = [composeFile],
              ProjectName = projectName,
              Timeout = 10
            }, TestContext.Current.CancellationToken);
        Assert.True(restartResult.Success,
            $"Restart failed: {restartResult.Error}");

        await Task.Delay(3000, TestContext.Current.CancellationToken);

        // Verify service is still running after restart
        var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName
        }, TestContext.Current.CancellationToken);
        Assert.True(listResult.Success);
        Assert.Contains(listResult.Data,
            s => s.State?.ToLower() == "running");
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
      }
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task Remove_StoppedContainers_RemovesSuccessfully()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");

      try
      {
        await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        }, TestContext.Current.CancellationToken);
        await Task.Delay(3000, TestContext.Current.CancellationToken);

        // Stop services first
        await ComposeDriver.StopAsync(Context, new ComposeStopConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Timeout = 10
        }, TestContext.Current.CancellationToken);

        // Remove stopped containers
        var removeResult = await ComposeDriver.RemoveAsync(Context,
            new ComposeRemoveConfig
            {
              ComposeFiles = [composeFile],
              ProjectName = projectName,
              Force = true
            }, TestContext.Current.CancellationToken);
        Assert.True(removeResult.Success,
            $"Remove failed: {removeResult.Error}");

        // Verify containers are gone
        var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          All = true
        }, TestContext.Current.CancellationToken);
        Assert.True(listResult.Success);
        Assert.True(listResult.Data.Count == 0,
            $"Expected 0 containers after remove, got {listResult.Data.Count}");
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
      }
    }

    #endregion

    #region TopAsync Tests

    [Fact]
    public async Task Top_RunningService_ReturnsProcesses()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");

      try
      {
        await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        }, TestContext.Current.CancellationToken);
        await Task.Delay(5000, TestContext.Current.CancellationToken);

        var topResult = await ComposeDriver.TopAsync(Context, new ComposeFileConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName
        }, TestContext.Current.CancellationToken);

        Assert.True(topResult.Success, $"Top failed: {topResult.Error}");
        Assert.NotNull(topResult.Data);
        Assert.True(topResult.Data.Count > 0, "Should have process info");
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
      }
    }

    #endregion

    #region ImagesAsync Tests

    [Fact]
    public async Task Images_RunningStack_ReturnsImageList()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");

      try
      {
        await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        }, TestContext.Current.CancellationToken);
        await Task.Delay(3000, TestContext.Current.CancellationToken);

        var imagesResult = await ComposeDriver.ImagesAsync(Context,
            new ComposeFileConfig
            {
              ComposeFiles = [composeFile],
              ProjectName = projectName
            }, TestContext.Current.CancellationToken);

        Assert.True(imagesResult.Success, $"Images failed: {imagesResult.Error}");
        Assert.NotNull(imagesResult.Data);
        Assert.True(imagesResult.Data.Count > 0, "Should list images");
        Assert.Contains(imagesResult.Data,
            img => img.Repository?.Contains("rabbitmq") == true);
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
      }
    }

    #endregion

    #region PortAsync Tests

    [Fact]
    public async Task Port_ExposedService_ReturnsMapping()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");

      try
      {
        await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        }, TestContext.Current.CancellationToken);
        await Task.Delay(5000, TestContext.Current.CancellationToken);

        var portResult = await ComposeDriver.PortAsync(Context, new ComposePortConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Service = "rabbitmq",
          PrivatePort = 5672
        }, TestContext.Current.CancellationToken);

        Assert.True(portResult.Success, $"Port failed: {portResult.Error}");
        Assert.NotNull(portResult.Data);
        Assert.Contains(":", portResult.Data); // e.g. "0.0.0.0:5672"
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
      }
    }

    #endregion

    #region PullAsync Tests

    [Fact]
    public async Task Pull_ServiceImages_Succeeds()
    {
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");

      var pullResult = await ComposeDriver.PullAsync(Context, new ComposePullConfig
      {
        ComposeFiles = [composeFile]
      }, TestContext.Current.CancellationToken);

      Assert.True(pullResult.Success, $"Pull failed: {pullResult.Error}");
    }

    #endregion

    #region RunAsync Tests

    [Fact]
    public async Task Run_OneOffCommand_ReturnsOutput()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");

      try
      {
        var runResult = await ComposeDriver.RunAsync(Context, new ComposeRunConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Service = "rabbitmq",
          Command = ["echo", "hello-from-run"],
          Rm = true,
          NoDeps = true,
          Tty = false
        }, TestContext.Current.CancellationToken);

        Assert.True(runResult.Success, $"Run failed: {runResult.Error}");
        Assert.Contains("hello-from-run", runResult.Data);
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
      }
    }

    #endregion

    #region CopyAsync Tests

    [Fact]
    public async Task Copy_FileToService_CopiesSuccessfully()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");
      var tempFile = Path.GetTempFileName();

      try
      {
        await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        }, TestContext.Current.CancellationToken);
        await Task.Delay(5000, TestContext.Current.CancellationToken);

        File.WriteAllText(tempFile, "test content from host");

        var copyResult = await ComposeDriver.CopyAsync(Context, new ComposeCopyConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          Source = tempFile,
          Destination = "rabbitmq:/tmp/testfile.txt"
        }, TestContext.Current.CancellationToken);

        Assert.True(copyResult.Success, $"Copy failed: {copyResult.Error}");

        // Verify file was copied via exec
        var execResult = await ComposeDriver.ExecuteAsync(Context,
            new ComposeExecConfig
            {
              ComposeFiles = [composeFile],
              ProjectName = projectName,
              Service = "rabbitmq",
              Command = ["cat", "/tmp/testfile.txt"],
              Tty = false
            }, TestContext.Current.CancellationToken);
        Assert.True(execResult.Success, $"Verify exec failed: {execResult.Error}");
        Assert.Contains("test content from host", execResult.Data);
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = [composeFile],
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
        if (File.Exists(tempFile))
          File.Delete(tempFile);
      }
    }

    #endregion
  }
}
