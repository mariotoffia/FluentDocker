using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  [Trait("Category", "Unit")]
  public class CliPruneOutputParserTests
  {
    [Fact]
    public void ParseImagePruneOutput_DockerFormat_ParsesDeletedAndSpace()
    {
      var output = @"Deleted Images:
untagged: alpine:latest
deleted: sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
95f4b9f8f3ac
Total reclaimed space: 12.5MB";

      var result = CliPruneOutputParser.ParseImagePruneOutput(output);

      Assert.Equal(3, result.ImagesDeleted.Count);
      Assert.Contains("alpine:latest", result.ImagesDeleted);
      Assert.Contains("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.ImagesDeleted);
      Assert.Contains("95f4b9f8f3ac", result.ImagesDeleted);
      Assert.Equal(12500000L, result.SpaceReclaimed);
    }

    [Fact]
    public void ParseImagePruneOutput_PodmanFormatWithoutHeaders_ParsesBareIds()
    {
      var output = @"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
4b825dc642cb
Total reclaimed space: 1.5GB";

      var result = CliPruneOutputParser.ParseImagePruneOutput(output);

      Assert.Equal(2, result.ImagesDeleted.Count);
      Assert.Contains("sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", result.ImagesDeleted);
      Assert.Contains("4b825dc642cb", result.ImagesDeleted);
      Assert.Equal(1500000000L, result.SpaceReclaimed);
    }

    [Theory]
    [InlineData("10KB", 10000L)]
    [InlineData("2MB", 2000000L)]
    [InlineData("3GB", 3000000000L)]
    [InlineData("1TB", 1000000000000L)]
    public void ParseImagePruneOutput_VariousReclaimedUnits_ParsesBytes(
        string reclaimedSpace, long expectedBytes)
    {
      var output = $"Deleted Images:\nsha256:cccccccccccc\nTotal reclaimed space: {reclaimedSpace}";

      var result = CliPruneOutputParser.ParseImagePruneOutput(output);

      Assert.Equal(expectedBytes, result.SpaceReclaimed);
    }

    [Fact]
    public void ParseNetworkPruneOutput_DockerFormat_ParsesDeletedNetworks()
    {
      var output = @"Deleted Networks:
fd-network-1
fd-network-2";

      var result = CliPruneOutputParser.ParseNetworkPruneOutput(output);

      Assert.Equal(2, result.NetworksDeleted.Count);
      Assert.Contains("fd-network-1", result.NetworksDeleted);
      Assert.Contains("fd-network-2", result.NetworksDeleted);
    }

    [Fact]
    public void ParseNetworkPruneOutput_PodmanFormatWithoutHeaders_ParsesDeletedNetworks()
    {
      var output = @"podman-net-a
podman-net-b";

      var result = CliPruneOutputParser.ParseNetworkPruneOutput(output);

      Assert.Equal(2, result.NetworksDeleted.Count);
      Assert.Contains("podman-net-a", result.NetworksDeleted);
      Assert.Contains("podman-net-b", result.NetworksDeleted);
    }

    [Fact]
    public void ParseVolumePruneOutput_DockerFormat_ParsesDeletedVolumesAndSpace()
    {
      var output = @"Deleted Volumes:
vol-a
vol-b
Total reclaimed space: 32MB";

      var result = CliPruneOutputParser.ParseVolumePruneOutput(output);

      Assert.Equal(2, result.VolumesDeleted.Count);
      Assert.Contains("vol-a", result.VolumesDeleted);
      Assert.Contains("vol-b", result.VolumesDeleted);
      Assert.Equal(32000000L, result.SpaceReclaimed);
    }

    [Fact]
    public void ParseSystemPruneOutput_AllSections_ParsesEverything()
    {
      var output = @"Deleted Containers:
container-1
Deleted Networks:
network-1
Deleted Images:
untagged: busybox:latest
deleted: sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd
e69de29bb2d1
Deleted Volumes:
volume-1
Deleted build cache objects:
cache-a
cache-b
Total reclaimed space: 2TB";

      var result = CliPruneOutputParser.ParseSystemPruneOutput(output);

      Assert.Single(result.ContainersDeleted);
      Assert.Single(result.NetworksDeleted);
      Assert.Equal(3, result.ImagesDeleted.Count);
      Assert.Single(result.VolumesDeleted);
      Assert.Equal(2, result.BuildCacheDeleted.Count);
      Assert.Equal(2000000000000L, result.SpaceReclaimed);
    }

    [Fact]
    public void ParseSystemPruneOutput_PartialSections_ParsesAvailableData()
    {
      var output = @"Deleted Images:
untagged: redis:7
Deleted build cache:
cache-1
Total reclaimed space: 500MB";

      var result = CliPruneOutputParser.ParseSystemPruneOutput(output);

      Assert.Empty(result.ContainersDeleted);
      Assert.Empty(result.NetworksDeleted);
      Assert.Single(result.ImagesDeleted);
      Assert.Equal("redis:7", result.ImagesDeleted[0]);
      Assert.Single(result.BuildCacheDeleted);
      Assert.Equal("cache-1", result.BuildCacheDeleted[0]);
      Assert.Equal(500000000L, result.SpaceReclaimed);
    }

    [Fact]
    public void ParseSystemPruneOutput_WindowsLineEndings_ParsesSections()
    {
      var output = "Deleted Containers:\r\nctr-win\r\nDeleted Volumes:\r\nvol-win\r\nTotal reclaimed space: 1GB\r\n";

      var result = CliPruneOutputParser.ParseSystemPruneOutput(output);

      Assert.Single(result.ContainersDeleted);
      Assert.Single(result.VolumesDeleted);
      Assert.Equal(1000000000L, result.SpaceReclaimed);
    }

    [Fact]
    public void ParseMethods_NullOrEmptyInput_ReturnsEmptyResults()
    {
      var imageNull = CliPruneOutputParser.ParseImagePruneOutput(null);
      var networkEmpty = CliPruneOutputParser.ParseNetworkPruneOutput("");
      var volumeNull = CliPruneOutputParser.ParseVolumePruneOutput(null);
      var systemEmpty = CliPruneOutputParser.ParseSystemPruneOutput("");

      Assert.Empty(imageNull.ImagesDeleted);
      Assert.Equal(0L, imageNull.SpaceReclaimed);
      Assert.Empty(networkEmpty.NetworksDeleted);
      Assert.Empty(volumeNull.VolumesDeleted);
      Assert.Equal(0L, volumeNull.SpaceReclaimed);
      Assert.Empty(systemEmpty.ContainersDeleted);
      Assert.Equal(0L, systemEmpty.SpaceReclaimed);
    }

    [Fact]
    public void ParseMethods_MalformedInput_ReturnsEmptyWithoutThrow()
    {
      var malformed = "<<<not-prune-output>>>";

      var image = CliPruneOutputParser.ParseImagePruneOutput(malformed);
      var network = CliPruneOutputParser.ParseNetworkPruneOutput(malformed);
      var volume = CliPruneOutputParser.ParseVolumePruneOutput(malformed);
      var system = CliPruneOutputParser.ParseSystemPruneOutput(malformed);

      Assert.Empty(image.ImagesDeleted);
      Assert.Equal(0L, image.SpaceReclaimed);
      Assert.Empty(network.NetworksDeleted);
      Assert.Empty(volume.VolumesDeleted);
      Assert.Empty(system.ContainersDeleted);
      Assert.Empty(system.ImagesDeleted);
      Assert.Empty(system.NetworksDeleted);
      Assert.Empty(system.VolumesDeleted);
      Assert.Empty(system.BuildCacheDeleted);
      Assert.Equal(0L, system.SpaceReclaimed);
    }
  }
}
