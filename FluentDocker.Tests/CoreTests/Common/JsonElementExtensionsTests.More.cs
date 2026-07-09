using System.Text.Json;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  public partial class JsonElementExtensionsTests
  {
    #region EnumerateArraySafe / EnumerateObjectSafe

    [Fact]
    public void EnumerateArraySafe_Array_Enumerates()
    {
      var el = JsonHelper.ParseElement("""[1,2,3]""");
      var count = 0;
      foreach (var _ in el.EnumerateArraySafe())
        count++;
      Assert.Equal(3, count);
    }

    [Fact]
    public void EnumerateArraySafe_NonArray_ReturnsEmpty()
    {
      var el = JsonHelper.ParseElement("""{"x":1}""");
      var count = 0;
      foreach (var _ in el.EnumerateArraySafe())
        count++;
      Assert.Equal(0, count);
    }

    [Fact]
    public void EnumerateObjectSafe_Object_Enumerates()
    {
      var el = JsonHelper.ParseElement("""{"a":1,"b":2}""");
      var count = 0;
      foreach (var _ in el.EnumerateObjectSafe())
        count++;
      Assert.Equal(2, count);
    }

    #endregion

    #region Deserialize<T>

    [Fact]
    public void Deserialize_ValidElement_ReturnsTyped()
    {
      var el = JsonHelper.ParseElement("""{"Name":"test","Value":42}""");
      var dto = el.Deserialize<TestDto>();
      Assert.NotNull(dto);
      Assert.Equal("test", dto!.Name);
      Assert.Equal(42, dto.Value);
    }

    #endregion

    #region IsNullOrUndefined / IsNullOrMissing

    [Fact]
    public void IsNullOrUndefined_NullElement_ReturnsTrue()
    {
      var el = JsonHelper.ParseElement("""{"x":null}""");
      var prop = el.Prop("x");
      Assert.NotNull(prop);
      Assert.True(prop!.Value.IsNullOrUndefined());
    }

    [Fact]
    public void IsNullOrUndefined_StringElement_ReturnsFalse()
    {
      var el = JsonHelper.ParseElement("""{"x":"val"}""");
      var prop = el.Prop("x");
      Assert.NotNull(prop);
      Assert.False(prop!.Value.IsNullOrUndefined());
    }

    [Fact]
    public void IsNullOrMissing_NullNullable_ReturnsTrue()
    {
      JsonElement? el = null;
      Assert.True(el.IsNullOrMissing());
    }

    [Fact]
    public void IsNullOrMissing_NullJsonValue_ReturnsTrue()
    {
      var root = JsonHelper.ParseElement("""{"x":null}""");
      var prop = root.Prop("x");
      Assert.True(prop.IsNullOrMissing());
    }

    #endregion

    #region ParseElement

    [Fact]
    public void ParseElement_ValidJson_ReturnsClonedElement()
    {
      var el = JsonHelper.ParseElement("""{"a":1}""");
      Assert.Equal(JsonValueKind.Object, el.ValueKind);
      Assert.Equal(1, el.GetInt32OrDefault("a"));
    }

    [Fact]
    public void ParseElement_Array_ReturnsArrayElement()
    {
      var el = JsonHelper.ParseElement("""[1,2,3]""");
      Assert.Equal(JsonValueKind.Array, el.ValueKind);
    }

    #endregion

    #region SerializeIndented

    [Fact]
    public void SerializeIndented_ProducesFormattedOutput()
    {
      var dto = new TestDto { Name = "test", Value = 1 };
      var json = JsonHelper.SerializeIndented(dto);
      Assert.Contains("\n", json);
      Assert.Contains("  ", json);
    }

    #endregion

  }
}
