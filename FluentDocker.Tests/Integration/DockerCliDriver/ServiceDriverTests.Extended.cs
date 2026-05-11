using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Extended integration tests for IServiceDriver: Update, Rollback, GetTasks, GetLogs.
  /// </summary>
  [Trait("Category", "DevLocal")]
  public partial class ServiceDriverTests
  {
    #region Update

    [Fact]
    public async Task Update_Image_ChangesServiceImage()
    {
      var serviceName = UniqueName("svc");
      try
      {
        await ServiceDriver.CreateAsync(Context,
            new ServiceCreateConfig
            {
              Name = serviceName,
              Image = NginxImage,
              Replicas = 1,
              Detach = true
            }, cancellationToken: TestContext.Current.CancellationToken);

        await WaitForServiceReplicasAsync(serviceName, 1, 30);

        // Update the image to alpine
        var updateResult = await ServiceDriver.UpdateAsync(Context,
            serviceName,
            new ServiceUpdateConfig
            {
              Image = TestImage,
              Detach = true
            }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(updateResult.Success,
            $"Update failed: {updateResult.Error}");

        // Wait for convergence and verify
        await Task.Delay(5000, cancellationToken: TestContext.Current.CancellationToken);
        var inspectResult = await ServiceDriver.InspectAsync(
            Context, serviceName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspectResult.Success);
        Assert.Contains("alpine", inspectResult.Data.Image);
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }

    [Fact]
    public async Task Update_AddEnvVar_Succeeds()
    {
      var serviceName = UniqueName("svc");
      try
      {
        await ServiceDriver.CreateAsync(Context,
            new ServiceCreateConfig
            {
              Name = serviceName,
              Image = NginxImage,
              Replicas = 1,
              Detach = true
            }, cancellationToken: TestContext.Current.CancellationToken);

        await WaitForServiceReplicasAsync(serviceName, 1, 30);

        var updateResult = await ServiceDriver.UpdateAsync(Context,
            serviceName,
            new ServiceUpdateConfig
            {
              EnvAdd = new Dictionary<string, string>
              {
                ["MY_TEST_VAR"] = "hello"
              },
              Detach = true
            }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(updateResult.Success,
            $"Update env failed: {updateResult.Error}");
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }

    #endregion

    #region Rollback

    [Fact]
    public async Task Rollback_AfterUpdate_Succeeds()
    {
      var serviceName = UniqueName("svc");
      try
      {
        // Create with nginx
        await ServiceDriver.CreateAsync(Context,
            new ServiceCreateConfig
            {
              Name = serviceName,
              Image = NginxImage,
              Replicas = 1,
              Detach = true
            }, cancellationToken: TestContext.Current.CancellationToken);
        await WaitForServiceReplicasAsync(serviceName, 1, 30);

        // Update to alpine
        await ServiceDriver.UpdateAsync(Context, serviceName,
            new ServiceUpdateConfig
            {
              Image = TestImage,
              Detach = true
            }, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(5000, cancellationToken: TestContext.Current.CancellationToken);

        // Rollback
        var rollbackResult = await ServiceDriver.RollbackAsync(
            Context, serviceName, detach: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(rollbackResult.Success,
            $"Rollback failed: {rollbackResult.Error}");

        // Verify we're back to nginx
        await Task.Delay(5000, cancellationToken: TestContext.Current.CancellationToken);
        var inspectResult = await ServiceDriver.InspectAsync(
            Context, serviceName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspectResult.Success);
        Assert.Contains("nginx", inspectResult.Data.Image);
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }

    #endregion

    #region GetTasks

    [Fact]
    public async Task GetTasks_RunningService_ReturnsTasks()
    {
      var serviceName = UniqueName("svc");
      try
      {
        await ServiceDriver.CreateAsync(Context,
            new ServiceCreateConfig
            {
              Name = serviceName,
              Image = NginxImage,
              Replicas = 2,
              Detach = true
            }, cancellationToken: TestContext.Current.CancellationToken);

        await WaitForServiceReplicasAsync(serviceName, 2, 60);

        var tasksResult = await ServiceDriver.GetTasksAsync(
            Context, serviceName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(tasksResult.Success,
            $"GetTasks failed: {tasksResult.Error}");
        Assert.NotNull(tasksResult.Data);
        Assert.True(tasksResult.Data.Count >= 2,
            $"Expected at least 2 tasks, got {tasksResult.Data.Count}");

        // Verify task properties
        foreach (var task in tasksResult.Data)
        {
          Assert.NotNull(task.Id);
          Assert.NotNull(task.Image);
        }
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }

    #endregion

    #region GetLogs

    [Fact]
    public async Task GetLogs_RunningService_ReturnsOutput()
    {
      var serviceName = UniqueName("svc");
      try
      {
        await ServiceDriver.CreateAsync(Context,
            new ServiceCreateConfig
            {
              Name = serviceName,
              Image = NginxImage,
              Replicas = 1,
              Detach = true
            }, cancellationToken: TestContext.Current.CancellationToken);

        await WaitForServiceReplicasAsync(serviceName, 1, 30);

        // Give nginx time to produce startup logs
        await Task.Delay(5000, cancellationToken: TestContext.Current.CancellationToken);

        var logsResult = await ServiceDriver.GetLogsAsync(Context,
            serviceName,
            new ServiceLogsConfig { Tail = 10 }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(logsResult.Success,
            $"GetLogs failed: {logsResult.Error}");
        // Nginx typically produces some startup output
        Assert.NotNull(logsResult.Data);
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Remove_NonExistentService_FailsGracefully()
    {
      var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
      var result = await ServiceDriver.RemoveAsync(
          Context, [fakeName], cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success,
          "Removing non-existent service should fail");
    }

    [Fact]
    public async Task Inspect_NonExistentService_FailsGracefully()
    {
      var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
      var result = await ServiceDriver.InspectAsync(Context, fakeName, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success,
          "Inspecting non-existent service should fail");
    }

    #endregion
  }
}
