using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public class JsonHelperProductionReadinessTests
  {
    [Theory]
    [MemberData(nameof(SharedOptions))]
    public void SharedOptions_AreReadOnly(JsonSerializerOptions options)
    {
      Assert.True(options.IsReadOnly);
      Assert.Throws<InvalidOperationException>(() =>
          options.Converters.Add(new JsonStringEnumConverter()));
    }

    [Fact]
    public void TryDeserializeOut_DistinguishesInvalidJsonFromJsonNull()
    {
      Assert.False(JsonHelper.TryDeserialize<SampleDto>("not json", out var invalid));
      Assert.Null(invalid);

      Assert.True(JsonHelper.TryDeserialize<SampleDto>("null", out var jsonNull));
      Assert.Null(jsonNull);
    }

    [Fact]
    public void TryGetIntProperty_NumberAsString_ReturnsValue()
    {
      Assert.Equal(42, JsonHelper.TryGetIntProperty("""{"exitCode":"42"}""", "exitCode"));
    }

    public static TheoryData<JsonSerializerOptions> SharedOptions() =>
        new()
        {
          JsonHelper.DefaultOptions,
          JsonHelper.CaseInsensitiveOptions,
          JsonHelper.IndentedOptions
        };

    private sealed class SampleDto
    {
      [JsonPropertyName("name")]
      public string? Name { get; set; }
    }
  }
}
