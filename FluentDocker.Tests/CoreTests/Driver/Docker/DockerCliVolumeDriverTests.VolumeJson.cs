using System;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Model.Volumes;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  public partial class DockerCliVolumeDriverTests
  {
    [Fact]
    public void ParseVolumeJson_DockerInspectShape_ParsesCreatedAtAndLenientOptions()
    {
      var json = """
        {
          "CreatedAt": "2026-07-03T10:11:12Z",
          "Driver": "local",
          "Labels": {"owner": "test"},
          "Mountpoint": "/var/lib/docker/volumes/myvol/_data",
          "Name": "myvol",
          "Options": {"o": "addr=10.0.0.1", "retries": 3},
          "Scope": "local"
        }
        """;

      var volume = JsonSerializer.Deserialize<Volume>(json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(volume);
      Assert.Equal(new DateTime(2026, 7, 3, 10, 11, 12, DateTimeKind.Utc), volume.Created);
      Assert.Equal("myvol", volume.Name);
      Assert.Equal("addr=10.0.0.1", volume.Options["o"]);
      Assert.Equal("3", volume.Options["retries"]);
    }
  }
}
