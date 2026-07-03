using System;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  public class DockerCliStreamDriverEventParsingTests
  {
    [Fact]
    public void ParseEventLine_RealDockerEventsJson_PopulatesActorAndTimestamp()
    {
      const string json =
          "{\"status\":\"start\",\"id\":\"legacy-id\",\"Type\":\"container\",\"Action\":\"start\"," +
          "\"Actor\":{\"ID\":\"container-id\",\"Attributes\":{\"image\":\"alpine\",\"name\":\"web\"}}," +
          "\"scope\":\"local\",\"time\":1710000000,\"timeNano\":1710000000123456789}";

      var result = DockerCliStreamDriver.ParseEventLine(json);

      Assert.NotNull(result);
      Assert.Equal("container-id", result.ActorId);
      Assert.Equal("alpine", result.ActorAttributes["image"]);
      Assert.Equal("web", result.ActorAttributes["name"]);
      Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1710000000).UtcDateTime, result.Timestamp);
      Assert.Equal(1710000000123456789, result.TimeNano);
    }
  }
}
