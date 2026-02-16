using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for IStackDriver (Docker CLI / Swarm mode).
  /// Marked DevLocal because they require Docker Swarm mode.
  /// </summary>
  [Trait("Category", "DevLocal")]
  public class StackDriverTests : SwarmTestBase
  {
    #region Deploy and Remove

    [Fact]
    public async Task Deploy_SimpleStack_Succeeds()
    {
      var stackName = UniqueName("stack");
      string tempDir = null;

      try
      {
        tempDir = CreateComposeDir(stackName);
        var composeFile = Path.Combine(tempDir, "docker-compose.yml");

        var result = await StackDriver.DeployAsync(Context,
            new StackDeployConfig
            {
              StackName = stackName,
              ComposeFiles = new List<string> { composeFile }
            }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, $"Deploy failed: {result.Error}");
        Assert.NotNull(result.Data);

        // Wait for service to converge
        await WaitForServiceReplicasAsync($"{stackName}_web", 1, 30);
      }
      finally
      {
        await RemoveStackSafeAsync(stackName);
        CleanupTempDir(tempDir);
      }
    }

    [Fact]
    public async Task Deploy_AndRemove_Succeeds()
    {
      var stackName = UniqueName("stack");
      string tempDir = null;

      try
      {
        tempDir = CreateComposeDir(stackName);
        var composeFile = Path.Combine(tempDir, "docker-compose.yml");

        await StackDriver.DeployAsync(Context,
            new StackDeployConfig
            {
              StackName = stackName,
              ComposeFiles = new List<string> { composeFile }
            }, cancellationToken: TestContext.Current.CancellationToken);

        await WaitForServiceReplicasAsync($"{stackName}_web", 1, 30);

        var removeResult = await StackDriver.RemoveAsync(
            Context, new[] { stackName }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(removeResult.Success, $"Remove failed: {removeResult.Error}");
      }
      finally
      {
        await RemoveStackSafeAsync(stackName);
        CleanupTempDir(tempDir);
      }
    }

    #endregion

    #region List

    [Fact]
    public async Task List_AfterDeploy_ReturnsStack()
    {
      var stackName = UniqueName("stack");
      string tempDir = null;

      try
      {
        tempDir = CreateComposeDir(stackName);
        var composeFile = Path.Combine(tempDir, "docker-compose.yml");

        await StackDriver.DeployAsync(Context,
            new StackDeployConfig
            {
              StackName = stackName,
              ComposeFiles = new List<string> { composeFile }
            }, cancellationToken: TestContext.Current.CancellationToken);

        await WaitForServiceReplicasAsync($"{stackName}_web", 1, 30);

        var listResult = await StackDriver.ListAsync(Context, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(listResult.Success, $"List failed: {listResult.Error}");
        Assert.NotNull(listResult.Data);
        Assert.Contains(listResult.Data, s => s.Name == stackName);
      }
      finally
      {
        await RemoveStackSafeAsync(stackName);
        CleanupTempDir(tempDir);
      }
    }

    #endregion

    #region GetServices

    [Fact]
    public async Task GetServices_AfterDeploy_ReturnsServices()
    {
      var stackName = UniqueName("stack");
      string tempDir = null;

      try
      {
        tempDir = CreateComposeDir(stackName);
        var composeFile = Path.Combine(tempDir, "docker-compose.yml");

        await StackDriver.DeployAsync(Context,
            new StackDeployConfig
            {
              StackName = stackName,
              ComposeFiles = new List<string> { composeFile }
            }, cancellationToken: TestContext.Current.CancellationToken);

        await WaitForServiceReplicasAsync($"{stackName}_web", 1, 30);

        var servicesResult = await StackDriver.GetServicesAsync(Context, stackName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(servicesResult.Success, $"GetServices failed: {servicesResult.Error}");
        Assert.NotNull(servicesResult.Data);
        Assert.NotEmpty(servicesResult.Data);
        Assert.Contains(servicesResult.Data, s => s.Name.Contains("web"));
      }
      finally
      {
        await RemoveStackSafeAsync(stackName);
        CleanupTempDir(tempDir);
      }
    }

    #endregion

    #region GetTasks

    [Fact]
    public async Task GetTasks_AfterDeploy_ReturnsTasks()
    {
      var stackName = UniqueName("stack");
      string tempDir = null;

      try
      {
        tempDir = CreateComposeDir(stackName);
        var composeFile = Path.Combine(tempDir, "docker-compose.yml");

        await StackDriver.DeployAsync(Context,
            new StackDeployConfig
            {
              StackName = stackName,
              ComposeFiles = new List<string> { composeFile }
            }, cancellationToken: TestContext.Current.CancellationToken);

        await WaitForServiceReplicasAsync($"{stackName}_web", 1, 30);

        var tasksResult = await StackDriver.GetTasksAsync(Context, stackName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(tasksResult.Success, $"GetTasks failed: {tasksResult.Error}");
        Assert.NotNull(tasksResult.Data);
        Assert.NotEmpty(tasksResult.Data);

        var task = tasksResult.Data[0];
        Assert.NotNull(task.Id);
        Assert.NotNull(task.Image);
      }
      finally
      {
        await RemoveStackSafeAsync(stackName);
        CleanupTempDir(tempDir);
      }
    }

    #endregion

    #region Deploy with Prune

    [Fact]
    public async Task Deploy_WithPrune_Succeeds()
    {
      var stackName = UniqueName("stack");
      string tempDir = null;

      try
      {
        tempDir = CreateComposeDir(stackName);
        var composeFile = Path.Combine(tempDir, "docker-compose.yml");

        // Initial deploy
        await StackDriver.DeployAsync(Context,
            new StackDeployConfig
            {
              StackName = stackName,
              ComposeFiles = new List<string> { composeFile }
            }, cancellationToken: TestContext.Current.CancellationToken);

        await WaitForServiceReplicasAsync($"{stackName}_web", 1, 30);

        // Re-deploy with prune (should still succeed)
        var result = await StackDriver.DeployAsync(Context,
            new StackDeployConfig
            {
              StackName = stackName,
              ComposeFiles = new List<string> { composeFile },
              Prune = true
            }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"Re-deploy with prune failed: {result.Error}");
      }
      finally
      {
        await RemoveStackSafeAsync(stackName);
        CleanupTempDir(tempDir);
      }
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Remove_NonExistentStack_FailsGracefully()
    {
      var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
      var result = await StackDriver.RemoveAsync(Context, new[] { fakeName }, cancellationToken: TestContext.Current.CancellationToken);
      // Docker may succeed or fail depending on version — verify semantics
      Assert.NotNull(result);
      if (!result.Success)
        Assert.False(string.IsNullOrEmpty(result.Error),
            "Failed result should include an error message");
    }

    #endregion

    #region Helpers

    private string CreateComposeDir(string stackName)
    {
      var tempDir = Path.Combine(Path.GetTempPath(),
          $"stack-test-{Guid.NewGuid():N}");
      Directory.CreateDirectory(tempDir);

      var composeContent =
          $"version: '3.8'\n" +
          $"services:\n" +
          $"  web:\n" +
          $"    image: {NginxImage}\n" +
          $"    deploy:\n" +
          $"      replicas: 1\n";

      File.WriteAllText(Path.Combine(tempDir, "docker-compose.yml"),
          composeContent);
      return tempDir;
    }

    private async Task RemoveStackSafeAsync(string stackName)
    {
      try
      { await StackDriver.RemoveAsync(Context, new[] { stackName }); }
      catch { }

      // Give Docker time to clean up services
      await Task.Delay(2000);
    }

    private static void CleanupTempDir(string tempDir)
    {
      if (tempDir != null && Directory.Exists(tempDir))
        try
        { Directory.Delete(tempDir, true); }
        catch { }
    }

    #endregion
  }
}
