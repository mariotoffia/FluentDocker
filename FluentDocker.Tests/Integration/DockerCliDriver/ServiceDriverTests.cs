using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for IServiceDriver (Docker CLI / Swarm mode).
  /// Marked DevLocal because they require Docker Swarm mode.
  /// </summary>
  [Trait("Category", "DevLocal")]
  public partial class ServiceDriverTests : SwarmTestBase
  {
    #region Create and Remove

    [Fact]
    public async Task Create_SimpleService_ReturnsId()
    {
      string? serviceName = null;
      try
      {
        serviceName = UniqueName("svc");
        var result = await ServiceDriver.CreateAsync(Context,
            new ServiceCreateConfig
            {
              Name = serviceName,
              Image = NginxImage,
              Replicas = 1,
              Detach = true
            }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, $"Create failed: {result.Error}");
        Assert.NotNull(result.Data);
        Assert.False(string.IsNullOrEmpty(result.Data.Id),
            "Service ID should not be empty");
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }

    [Fact]
    public async Task CreateAndRemove_Succeeds()
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

        var removeResult = await ServiceDriver.RemoveAsync(
            Context, new[] { serviceName }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(removeResult.Success, $"Remove failed: {removeResult.Error}");
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }

    #endregion

    #region List

    [Fact]
    public async Task List_AfterCreate_ReturnsService()
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

        var listResult = await ServiceDriver.ListAsync(Context, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(listResult.Success, $"List failed: {listResult.Error}");
        Assert.NotNull(listResult.Data);
        Assert.Contains(listResult.Data, s => s.Name == serviceName);
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }

    [Fact]
    public async Task List_WithNameFilter_ReturnsOnlyMatching()
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

        var listResult = await ServiceDriver.ListAsync(Context,
            new ServiceListFilter { Name = serviceName }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(listResult.Success, $"List failed: {listResult.Error}");
        Assert.Single(listResult.Data);
        Assert.Equal(serviceName, listResult.Data[0].Name);
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }

    #endregion

    #region Inspect

    [Fact]
    public async Task Inspect_ExistingService_ReturnsDetails()
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
              Detach = true,
              Labels = new Dictionary<string, string>
              {
                ["test-label"] = "test-value"
              }
            }, cancellationToken: TestContext.Current.CancellationToken);

        await WaitForServiceReplicasAsync(serviceName, 1, 30);

        var inspectResult = await ServiceDriver.InspectAsync(
            Context, serviceName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspectResult.Success,
            $"Inspect failed: {inspectResult.Error}");
        Assert.NotNull(inspectResult.Data);
        Assert.Equal(serviceName, inspectResult.Data.Name);
        Assert.NotNull(inspectResult.Data.Id);
        Assert.True(inspectResult.Data.Version > 0,
            "Service version should be positive");
        Assert.Contains("nginx", inspectResult.Data.Image);
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }

    #endregion

    #region Scale

    [Fact]
    public async Task Scale_SingleService_UpdatesReplicas()
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

        // Scale up to 3
        var scaleResult = await ServiceDriver.ScaleAsync(Context,
            new Dictionary<string, int> { [serviceName] = 3 },
            detach: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(scaleResult.Success,
            $"Scale failed: {scaleResult.Error}");

        await WaitForServiceReplicasAsync(serviceName, 3, 60);

        // Verify via inspect
        var inspectResult = await ServiceDriver.InspectAsync(
            Context, serviceName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspectResult.Success);
        Assert.Equal(3, inspectResult.Data.Replicas);
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }

    #endregion

    #region Helpers

    private async Task RemoveServiceSafeAsync(string serviceName)
    {
      if (!string.IsNullOrEmpty(serviceName))
        try
        {
          await ServiceDriver.RemoveAsync(Context, new[] { serviceName });
        }
        catch { }
    }

    #endregion
  }
}
