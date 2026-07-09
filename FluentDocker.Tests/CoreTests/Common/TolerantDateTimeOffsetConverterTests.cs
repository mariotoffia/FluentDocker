using System;
using System.Collections.Generic;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Volumes;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public sealed class TolerantDateTimeOffsetConverterTests
  {
    [Fact]
    public void TryDeserialize_ContainerWithBadStartedAt_PreservesContainerAndDefaultsDate()
    {
      var json = """
          [{
            "Id": "abc123",
            "State": {
              "StartedAt": "2021-13-45T99:99:99Z"
            }
          }]
          """;

      var ok = JsonHelper.TryDeserialize<List<Container>>(json, out var containers);

      Assert.True(ok);
      var container = Assert.Single(containers!);
      Assert.Equal("abc123", container.Id);
      Assert.Equal(default, container.State!.StartedAt);
    }

    [Fact]
    public void TryDeserialize_VolumeWithBadCreatedAt_PreservesVolumeAndDefaultsDate()
    {
      var json = """
          [{
            "Name": "data",
            "CreatedAt": "not-a-date"
          }]
          """;

      var ok = JsonHelper.TryDeserialize<List<Volume>>(json, out var volumes);

      Assert.True(ok);
      var volume = Assert.Single(volumes!);
      Assert.Equal("data", volume.Name);
      Assert.Equal(default, volume.Created);
    }

    [Fact]
    public void TryDeserialize_ContainerWithObjectAsStartedAt_PreservesContainerAndDefaultsDate()
    {
      var json = """
          [{
            "Id": "abc123",
            "State": {
              "StartedAt": { "unexpected": "object" }
            }
          }]
          """;

      var ok = JsonHelper.TryDeserialize<List<Container>>(json, out var containers);

      Assert.True(ok);
      var container = Assert.Single(containers!);
      Assert.Equal("abc123", container.Id);
      Assert.Equal(default, container.State!.StartedAt);
    }

    [Fact]
    public void TryDeserialize_ContainerWithDockerNanosecondTimestamp_RoundsToHundredNanosecondTick()
    {
      // Docker emits 9 fractional digits (nanoseconds); .NET resolves to 100ns ticks. The
      // tolerant converter parses via DateTimeOffset.TryParse, which ROUNDS the sub-tick
      // remainder, so 0.123456789s resolves to .1234568 (1234568 ticks past the second).
      // This hardcoded expectation also differs from the strict built-in converter (which
      // truncates to .1234567), so it fails if the tolerant converter is removed.
      var json = """
          [{
            "Id": "abc123",
            "State": {
              "StartedAt": "2021-01-01T00:00:00.123456789Z"
            }
          }]
          """;

      var expected = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1234568);

      var ok = JsonHelper.TryDeserialize<List<Container>>(json, out var containers);

      Assert.True(ok);
      var container = Assert.Single(containers!);
      Assert.Equal("abc123", container.Id);
      Assert.Equal(expected, container.State!.StartedAt);
    }
  }
}
