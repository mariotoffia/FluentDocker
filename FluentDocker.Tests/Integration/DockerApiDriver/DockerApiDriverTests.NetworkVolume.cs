using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerApiDriver
{
  /// <summary>
  /// Docker API driver integration tests for network, volume, and system operations.
  /// </summary>
  public partial class DockerApiDriverTests
  {
    #region Network Connect/Disconnect Tests

    [Fact]
    public async Task Network_ConnectAndDisconnect_Succeeds()
    {
      string containerId = null;
      string networkId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);

        var networkResult = await NetworkDriver.CreateAsync(Context,
            new NetworkCreateConfig
            {
              Name = UniqueName("api-net")
            }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(networkResult.Success,
            $"Network create failed: {networkResult.Error}");
        networkId = networkResult.Data.Id;

        // Connect
        var connectResult = await NetworkDriver.ConnectAsync(
            Context, networkId, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(connectResult.Success,
            $"Connect failed: {connectResult.Error}");

        // Verify via inspect
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
        Assert.NotNull(inspect.Data.NetworkSettings?.Networks);

        // Disconnect
        var disconnectResult = await NetworkDriver.DisconnectAsync(
            Context, networkId, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(disconnectResult.Success,
            $"Disconnect failed: {disconnectResult.Error}");
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
        if (!string.IsNullOrEmpty(networkId))
          await NetworkDriver.RemoveAsync(Context, networkId, cancellationToken: TestContext.Current.CancellationToken);
      }
    }

    #endregion

    #region Network Inspect Tests

    [Fact]
    public async Task Network_Inspect_ReturnsDetails()
    {
      string networkId = null;
      try
      {
        var networkResult = await NetworkDriver.CreateAsync(Context,
            new NetworkCreateConfig
            {
              Name = UniqueName("api-inspect")
            }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(networkResult.Success);
        networkId = networkResult.Data.Id;

        var inspectResult = await NetworkDriver.InspectAsync(
            Context, networkId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(inspectResult.Success,
            $"Inspect failed: {inspectResult.Error}");
        Assert.NotNull(inspectResult.Data);
        Assert.NotNull(inspectResult.Data.Name);
      }
      finally
      {
        if (!string.IsNullOrEmpty(networkId))
          await NetworkDriver.RemoveAsync(Context, networkId, cancellationToken: TestContext.Current.CancellationToken);
      }
    }

    [Fact]
    public async Task Network_Inspect_NonExistent_Fails()
    {
      var fakeId = "nonexistent" + Guid.NewGuid().ToString("N")[..12];
      var result = await NetworkDriver.InspectAsync(Context, fakeId, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
    }

    #endregion

    #region Network Prune Tests

    [Fact]
    public async Task Network_Prune_Succeeds()
    {
      var pruneResult = await NetworkDriver.PruneAsync(Context, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(pruneResult.Success,
          $"Prune failed: {pruneResult.Error}");
    }

    #endregion

    #region Volume Inspect Tests

    [Fact]
    public async Task Volume_Inspect_ReturnsDetails()
    {
      string volumeName = null;
      try
      {
        volumeName = UniqueName("api-vol");
        var createResult = await VolumeDriver.CreateAsync(Context,
            new VolumeCreateConfig { Name = volumeName }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(createResult.Success);

        var inspectResult = await VolumeDriver.InspectAsync(
            Context, volumeName, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(inspectResult.Success,
            $"Inspect failed: {inspectResult.Error}");
        Assert.NotNull(inspectResult.Data);
        Assert.Equal(volumeName, inspectResult.Data.Name);
      }
      finally
      {
        if (!string.IsNullOrEmpty(volumeName))
          await VolumeDriver.RemoveAsync(Context, volumeName, force: true, cancellationToken: TestContext.Current.CancellationToken);
      }
    }

    [Fact]
    public async Task Volume_Inspect_NonExistent_Fails()
    {
      var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
      var result = await VolumeDriver.InspectAsync(Context, fakeName, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
    }

    #endregion

    #region Volume Prune Tests

    [Fact]
    public async Task Volume_Prune_Succeeds()
    {
      var pruneResult = await VolumeDriver.PruneAsync(Context, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(pruneResult.Success,
          $"Prune failed: {pruneResult.Error}");
    }

    #endregion

    #region System DiskUsage Tests

    [Fact]
    public async Task System_GetDiskUsage_ReturnsInfo()
    {
      var result = await SystemDriver.GetDiskUsageAsync(Context, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success,
          $"DiskUsage failed: {result.Error}");
      Assert.NotNull(result.Data);
    }

    #endregion

    #region System IsLinuxEngine Tests

    [Fact]
    public async Task System_IsLinuxEngine_ReturnsBool()
    {
      var result = await SystemDriver.IsLinuxEngineAsync(Context, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success,
          $"IsLinuxEngine failed: {result.Error}");
      // On macOS/Linux, this should be true
    }

    #endregion
  }
}
