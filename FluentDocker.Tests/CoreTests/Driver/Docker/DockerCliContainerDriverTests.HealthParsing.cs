using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
using Xunit;
using Container = FluentDocker.Model.Containers.Container;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  public partial class DockerCliContainerDriverTests
  {
    [Theory]
    [InlineData("none", HealthState.None)]
    [InlineData("future-docker-status", HealthState.Unknown)]
    public void InspectParsing_HealthStatus_ToleratesDockerValues(
      string status, HealthState expected)
    {
      var json = $$"""
        [{
          "Id": "abc123",
          "State": {
            "Health": {
              "Status": "{{status}}"
            }
          }
        }]
        """;

      var containers = JsonSerializer.Deserialize<List<Container>>(
          json, JsonHelper.CaseInsensitiveOptions);
      var container = containers?.FirstOrDefault();

      Assert.NotNull(container);
      Assert.Equal(expected, container.State.Health.Status);
    }

    [Fact]
    public void InspectParsing_HealthStatusMissing_DefaultsToUnknown()
    {
      var health = JsonSerializer.Deserialize<Health>(
          "{}", JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(health);
      Assert.Equal(HealthState.Unknown, health.Status);
    }
  }
}
