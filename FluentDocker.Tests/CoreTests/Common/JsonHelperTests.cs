using System.Text.Json.Serialization;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public class JsonHelperTests
  {
    #region TryDeserialize<T>(string)

    [Fact]
    public void TryDeserialize_ValidJson_DeserializesCorrectly()
    {
      var json = """{"name":"test","count":42}""";
      var result = JsonHelper.TryDeserialize<SampleDto>(json);

      Assert.NotNull(result);
      Assert.Equal("test", result.Name);
      Assert.Equal(42, result.Count);
    }

    [Fact]
    public void TryDeserialize_CamelCase_HandledByCaseInsensitive()
    {
      var json = """{"Name":"upper","Count":10}""";
      var result = JsonHelper.TryDeserialize<SampleDto>(json);

      Assert.NotNull(result);
      Assert.Equal("upper", result.Name);
      Assert.Equal(10, result.Count);
    }

    [Fact]
    public void TryDeserialize_InvalidJson_ReturnsDefault()
    {
      var result = JsonHelper.TryDeserialize<SampleDto>("not json {{{");
      Assert.Null(result);
    }

    [Fact]
    public void TryDeserialize_NullString_ReturnsDefault()
    {
      var result = JsonHelper.TryDeserialize<SampleDto>((string)null);
      Assert.Null(result);
    }

    [Fact]
    public void TryDeserialize_EmptyString_ReturnsDefault()
    {
      var result = JsonHelper.TryDeserialize<SampleDto>("");
      Assert.Null(result);
    }

    [Fact]
    public void TryDeserialize_NumberAsString_Handled()
    {
      // Docker sometimes returns numbers as strings in JSON
      var json = """{"name":"x","count":"99"}""";
      var result = JsonHelper.TryDeserialize<SampleDto>(json);

      Assert.NotNull(result);
      Assert.Equal(99, result.Count);
    }

    #endregion

    #region TryDeserialize<T>(ReadOnlySpan<byte>)

    [Fact]
    public void TryDeserialize_Utf8Bytes_Works()
    {
      var bytes = System.Text.Encoding.UTF8.GetBytes("""{"name":"bytes","count":7}""");
      var result = JsonHelper.TryDeserialize<SampleDto>(bytes);

      Assert.NotNull(result);
      Assert.Equal("bytes", result.Name);
      Assert.Equal(7, result.Count);
    }

    [Fact]
    public void TryDeserialize_EmptyBytes_ReturnsDefault()
    {
      var result = JsonHelper.TryDeserialize<SampleDto>([]);
      Assert.Null(result);
    }

    #endregion

    #region Serialize

    [Fact]
    public void Serialize_ProducesCamelCaseJson()
    {
      var dto = new SampleDto { Name = "test", Count = 5 };
      var json = JsonHelper.Serialize(dto);

      Assert.Contains("\"name\":", json);
      Assert.Contains("\"count\":", json);
      Assert.DoesNotContain("\"Name\":", json);
    }

    [Fact]
    public void Serialize_NullProperties_Omitted()
    {
      var dto = new SampleDto { Name = null, Count = 0 };
      var json = JsonHelper.Serialize(dto);

      Assert.DoesNotContain("\"name\":", json);
      Assert.Contains("\"count\":", json);
    }

    [Fact]
    public void SerializeToUtf8Bytes_ReturnsNonEmpty()
    {
      var dto = new SampleDto { Name = "utf8", Count = 1 };
      var bytes = JsonHelper.SerializeToUtf8Bytes(dto);

      Assert.NotNull(bytes);
      Assert.True(bytes.Length > 0);
    }

    #endregion

    #region TryGetProperty

    [Fact]
    public void TryGetProperty_ExistingStringProp_ReturnsValue()
    {
      var json = """{"id":"abc123","status":"running"}""";
      Assert.Equal("abc123", JsonHelper.TryGetProperty(json, "id"));
      Assert.Equal("running", JsonHelper.TryGetProperty(json, "status"));
    }

    [Fact]
    public void TryGetProperty_MissingProp_ReturnsNull()
    {
      var json = """{"id":"abc123"}""";
      Assert.Null(JsonHelper.TryGetProperty(json, "missing"));
    }

    [Fact]
    public void TryGetProperty_InvalidJson_ReturnsNull()
    {
      Assert.Null(JsonHelper.TryGetProperty("not json", "id"));
    }

    [Fact]
    public void TryGetProperty_NullInput_ReturnsNull()
    {
      Assert.Null(JsonHelper.TryGetProperty(null, "id"));
    }

    #endregion

    #region TryGetIntProperty

    [Fact]
    public void TryGetIntProperty_ExistingInt_ReturnsValue()
    {
      var json = """{"exitCode":42}""";
      Assert.Equal(42, JsonHelper.TryGetIntProperty(json, "exitCode"));
    }

    [Fact]
    public void TryGetIntProperty_Missing_ReturnsNull()
    {
      var json = """{"exitCode":42}""";
      Assert.Null(JsonHelper.TryGetIntProperty(json, "missing"));
    }

    [Fact]
    public void TryGetIntProperty_NotAnInt_ReturnsNull()
    {
      var json = """{"exitCode":"not a number"}""";
      Assert.Null(JsonHelper.TryGetIntProperty(json, "exitCode"));
    }

    #endregion

    #region Options Consistency

    [Fact]
    public void DefaultOptions_AreSameInstance()
    {
      var a = JsonHelper.DefaultOptions;
      var b = JsonHelper.DefaultOptions;
      Assert.Same(a, b);
    }

    [Fact]
    public void CaseInsensitiveOptions_AreSameInstance()
    {
      var a = JsonHelper.CaseInsensitiveOptions;
      var b = JsonHelper.CaseInsensitiveOptions;
      Assert.Same(a, b);
    }

    #endregion

    private class SampleDto
    {
      [JsonPropertyName("name")]
      public string Name { get; set; }

      [JsonPropertyName("count")]
      public int Count { get; set; }
    }
  }
}
