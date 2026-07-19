using FluentDocker.Common;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// ML-8: the container inspect DTO must capture unmodeled fields (e.g. <c>HostConfig</c>)
  /// via JSON extension data instead of silently dropping them, so consumers do not need to
  /// re-parse the raw inspect output.
  /// </summary>
  [Trait("Category", "Unit")]
  public sealed class ContainerInspectExtensionDataTests
  {
    [Fact]
    public void Deserialize_UnmodeledInspectFields_AreCapturedInAdditionalData()
    {
      var json = """
          {
            "Id": "abc123",
            "Name": "web",
            "HostConfig": { "Memory": 1024, "Privileged": true },
            "GraphDriver": { "Name": "overlay2" },
            "Config": { "Image": "nginx", "Healthcheck": { "Test": ["CMD", "true"] } }
          }
          """;

      var container = JsonHelper.TryDeserialize<Container>(json);

      Assert.NotNull(container);
      Assert.Equal("abc123", container.Id);
      Assert.NotNull(container.AdditionalData);
      Assert.Equal(1024, container.AdditionalData["HostConfig"].GetProperty("Memory").GetInt64());
      Assert.True(container.AdditionalData.ContainsKey("GraphDriver"));

      Assert.NotNull(container.Config);
      Assert.Equal("nginx", container.Config.Image);
      Assert.NotNull(container.Config.AdditionalData);
      Assert.True(container.Config.AdditionalData.ContainsKey("Healthcheck"));
    }

    [Fact]
    public void Deserialize_FullyModeledInspect_LeavesAdditionalDataNull()
    {
      var json = """{ "Id": "abc123", "Name": "web" }""";

      var container = JsonHelper.TryDeserialize<Container>(json);

      Assert.NotNull(container);
      Assert.Null(container.AdditionalData);
    }

    [Fact]
    public void SerializeDeserialize_RoundTripsAdditionalData()
    {
      var json = """
          {
            "Id": "abc123",
            "HostConfig": { "Memory": 2048 }
          }
          """;
      var container = JsonHelper.TryDeserialize<Container>(json);
      Assert.NotNull(container);

      var roundTripped = JsonHelper.TryDeserialize<Container>(JsonHelper.Serialize(container));

      Assert.NotNull(roundTripped);
      Assert.Equal("abc123", roundTripped.Id);
      Assert.NotNull(roundTripped.AdditionalData);
      Assert.Equal(2048, roundTripped.AdditionalData["HostConfig"].GetProperty("Memory").GetInt64());
    }
  }
}
