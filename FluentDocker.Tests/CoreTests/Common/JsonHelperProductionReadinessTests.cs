using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
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

    [Fact]
    public void TryDeserialize_ContainerPortsWithMalformedHostEndpoint_DoesNotThrow()
    {
      var json = """
          {
            "Ports": {
              "8080/tcp": [
                { "HostIp": "not an ip", "HostPort": "not a port" }
              ]
            }
          }
          """;

      var ok = JsonHelper.TryDeserialize<ContainerNetworkSettings>(json, out var settings);

      Assert.True(ok);
      var endpoint = Assert.Single(settings.Ports["8080/tcp"]);
      Assert.Equal("not an ip", endpoint.HostIp);
      Assert.Equal("not a port", endpoint.HostPort);
    }

    [Fact]
    public void TryDeserialize_SetterArgumentException_ReturnsFalse()
    {
      var ok = JsonHelper.TryDeserialize<ThrowingSetterDto>("""{"Name":"boom"}""", out var value);

      Assert.False(ok);
      Assert.Null(value);
    }

    [Fact]
    public void TryDeserialize_SetterFormatException_ReturnsFalse()
    {
      var ok = JsonHelper.TryDeserialize<ThrowingFormatDto>("""{"Name":"boom"}""", out var value);

      Assert.False(ok);
      Assert.Null(value);
    }

    [Fact]
    public void TryDeserialize_SetterOverflowException_ReturnsFalse()
    {
      var ok = JsonHelper.TryDeserialize<ThrowingOverflowDto>("""{"Name":"boom"}""", out var value);

      Assert.False(ok);
      Assert.Null(value);
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

    private sealed class ThrowingSetterDto
    {
      private readonly string? _name = string.Empty;

      public string? Name
      {
        get => _name;
        set => throw new ArgumentException("bad value", nameof(value));
      }
    }

    private sealed class ThrowingFormatDto
    {
      private readonly string? _name = string.Empty;

      public string? Name
      {
        get => _name;
        set => throw new FormatException("bad format");
      }
    }

    private sealed class ThrowingOverflowDto
    {
      private readonly string? _name = string.Empty;

      public string? Name
      {
        get => _name;
        set => throw new OverflowException("too large");
      }
    }
  }
}
