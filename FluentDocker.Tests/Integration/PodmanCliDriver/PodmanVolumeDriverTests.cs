using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Integration tests for Podman volume driver.
  /// Requires Podman to be installed.
  /// </summary>
  [Collection("PodmanDriver")]
  [Trait("Category", "PodmanIntegration")]
  public class PodmanVolumeDriverTests : PodmanDriverTestBase
  {
    [Fact]
    public async Task CreateAndRemove_Succeeds()
    {
      var name = UniqueName("vol");
      try
      {
        var config = new VolumeCreateConfig { Name = name };
        var createResult = await VolumeDriver.CreateAsync(Context, config);
        Assert.True(createResult.Success, $"Create failed: {createResult.Error}");
        Assert.Equal(name, createResult.Data.Name);
      }
      finally
      {
        await VolumeDriver.RemoveAsync(Context, name, force: true);
      }
    }

    [Fact]
    public async Task InspectVolume_ReturnsDetails()
    {
      var name = UniqueName("vol");
      try
      {
        await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig { Name = name });

        var result = await VolumeDriver.InspectAsync(Context, name);
        Assert.True(result.Success, $"Inspect failed: {result.Error}");
        Assert.Equal(name, result.Data.Name);
      }
      finally
      {
        await VolumeDriver.RemoveAsync(Context, name, force: true);
      }
    }

    [Fact]
    public async Task ListVolumes_ReturnsResults()
    {
      var name = UniqueName("vol");
      try
      {
        await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig { Name = name });

        var result = await VolumeDriver.ListAsync(Context);
        Assert.True(result.Success, $"List failed: {result.Error}");
        Assert.NotEmpty(result.Data);
      }
      finally
      {
        await VolumeDriver.RemoveAsync(Context, name, force: true);
      }
    }
  }
}
