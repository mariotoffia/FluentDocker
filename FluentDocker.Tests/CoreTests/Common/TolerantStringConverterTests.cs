using System.Text.Json;
using System.Text.Json.Serialization;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  /// <summary>
  /// Unit tests for <see cref="TolerantStringConverter"/> (issue #335): JSON numbers
  /// and booleans landing in <see cref="string"/> properties must be read leniently
  /// instead of throwing, while genuine strings and nulls pass through unchanged.
  /// </summary>
  [Trait("Category", "Unit")]
  public class TolerantStringConverterTests
  {
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
      var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
      // TolerantStringConverter is [Obsolete] (COMMON-2: unused production dead code) but these
      // tests deliberately exercise the converter's behavior, so suppress the obsolete-usage error.
#pragma warning disable CS0618
      o.Converters.Add(new TolerantStringConverter());
#pragma warning restore CS0618
      return o;
    }

    private sealed class Holder
    {
      public string? Value { get; set; }
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("16", "16")]
    [InlineData("-1", "-1")]
    [InlineData("1500000000", "1500000000")]
    [InlineData("3.14", "3.14")]
    public void Read_JsonNumber_PreservesLiteralText(string jsonNumber, string expected)
    {
      var holder = JsonSerializer.Deserialize<Holder>($$"""{"Value": {{jsonNumber}} }""", Options);
      Assert.NotNull(holder);
      Assert.Equal(expected, holder.Value);
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    public void Read_JsonBoolean_BecomesLiteralText(string jsonBool, string expected)
    {
      var holder = JsonSerializer.Deserialize<Holder>($$"""{"Value": {{jsonBool}} }""", Options);
      Assert.NotNull(holder);
      Assert.Equal(expected, holder.Value);
    }

    [Fact]
    public void Read_JsonString_PassesThrough()
    {
      var holder = JsonSerializer.Deserialize<Holder>("""{"Value":"hello"}""", Options);
      Assert.NotNull(holder);
      Assert.Equal("hello", holder.Value);
    }

    [Fact]
    public void Read_JsonNull_BecomesNull()
    {
      var holder = JsonSerializer.Deserialize<Holder>("""{"Value":null}""", Options);
      Assert.NotNull(holder);
      Assert.Null(holder.Value);
    }

    [Fact]
    public void Write_String_RoundTrips()
    {
      var json = JsonSerializer.Serialize(new Holder { Value = "abc" }, Options);
      Assert.Contains("\"abc\"", json);
    }

    [Fact]
    public void Write_Null_WritesJsonNull()
    {
      var json = JsonSerializer.Serialize(new Holder { Value = null }, Options);
      Assert.Contains("null", json);
    }
  }
}
