using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public partial class JsonElementExtensionsTests
  {
    #region Prop

    [Fact]
    public void Prop_ExistingProperty_ReturnsElement()
    {
      var el = JsonHelper.ParseElement("""{"name":"hello"}""");
      var prop = el.Prop("name");
      Assert.NotNull(prop);
      Assert.Equal("hello", prop.Value.GetString());
    }

    [Fact]
    public void Prop_MissingProperty_ReturnsNull()
    {
      var el = JsonHelper.ParseElement("""{"name":"hello"}""");
      Assert.Null(el.Prop("missing"));
    }

    [Fact]
    public void Prop_OnNonObject_ReturnsNull()
    {
      var el = JsonHelper.ParseElement("""[1,2,3]""");
      Assert.Null(el.Prop("name"));
    }

    [Fact]
    public void Prop_FallbackName_UsesSecondName()
    {
      var el = JsonHelper.ParseElement("""{"ID":"abc"}""");
      var prop = el.Prop("Id", "ID");
      Assert.NotNull(prop);
      Assert.Equal("abc", prop.Value.GetString());
    }

    [Fact]
    public void Prop_FallbackName_PrefersFirst()
    {
      var el = JsonHelper.ParseElement("""{"Id":"first","ID":"second"}""");
      var prop = el.Prop("Id", "ID");
      Assert.NotNull(prop);
      Assert.Equal("first", prop!.Value.GetString());
    }

    [Fact]
    public void Prop_ThreeNames_FallsToThird()
    {
      var el = JsonHelper.ParseElement("""{"id":"found"}""");
      var prop = el.Prop("Id", "ID", "id");
      Assert.NotNull(prop);
      Assert.Equal("found", prop.Value.GetString());
    }

    #endregion

    #region GetStringOrDefault

    [Fact]
    public void GetStringOrDefault_ExistingString_ReturnsValue()
    {
      var el = JsonHelper.ParseElement("""{"name":"test"}""");
      Assert.Equal("test", el.GetStringOrDefault("name"));
    }

    [Fact]
    public void GetStringOrDefault_MissingProp_ReturnsNull()
    {
      var el = JsonHelper.ParseElement("""{"name":"test"}""");
      Assert.Null(el.GetStringOrDefault("missing"));
    }

    [Fact]
    public void GetStringOrDefault_NonStringProp_ReturnsNull()
    {
      var el = JsonHelper.ParseElement("""{"count":42}""");
      Assert.Null(el.GetStringOrDefault("count"));
    }

    [Fact]
    public void GetStringOrDefault_TwoNames_FallsBack()
    {
      var el = JsonHelper.ParseElement("""{"Name":"found"}""");
      Assert.Equal("found", el.GetStringOrDefault("name", "Name"));
    }

    #endregion

    #region GetInt32OrDefault

    [Fact]
    public void GetInt32OrDefault_Number_ReturnsValue()
    {
      var el = JsonHelper.ParseElement("""{"count":42}""");
      Assert.Equal(42, el.GetInt32OrDefault("count"));
    }

    [Fact]
    public void GetInt32OrDefault_StringNumber_ParsesValue()
    {
      var el = JsonHelper.ParseElement("""{"count":"99"}""");
      Assert.Equal(99, el.GetInt32OrDefault("count"));
    }

    [Fact]
    public void GetInt32OrDefault_Missing_ReturnsDefault()
    {
      var el = JsonHelper.ParseElement("""{"other":1}""");
      Assert.Equal(0, el.GetInt32OrDefault("count"));
      Assert.Equal(-1, el.GetInt32OrDefault("count", -1));
    }

    #endregion

    #region GetInt64OrDefault

    [Fact]
    public void GetInt64OrDefault_LargeNumber_ReturnsValue()
    {
      var el = JsonHelper.ParseElement("""{"bytes":8589934592}""");
      Assert.Equal(8589934592L, el.GetInt64OrDefault("bytes"));
    }

    [Fact]
    public void GetInt64OrDefault_StringNumber_ParsesValue()
    {
      var el = JsonHelper.ParseElement("""{"bytes":"8589934592"}""");
      Assert.Equal(8589934592L, el.GetInt64OrDefault("bytes"));
    }

    #endregion

    #region GetUInt64OrDefault

    [Fact]
    public void GetUInt64OrDefault_ReturnsValue()
    {
      var el = JsonHelper.ParseElement("""{"val":18446744073709551615}""");
      Assert.Equal(ulong.MaxValue, el.GetUInt64OrDefault("val"));
    }

    #endregion

    #region GetDoubleOrDefault

    [Fact]
    public void GetDoubleOrDefault_Number_ReturnsValue()
    {
      var el = JsonHelper.ParseElement("""{"pct":3.14}""");
      Assert.Equal(3.14, el.GetDoubleOrDefault("pct"), 2);
    }

    [Fact]
    public void GetDoubleOrDefault_StringNumber_ParsesValue()
    {
      var el = JsonHelper.ParseElement("""{"pct":"3.14"}""");
      Assert.Equal(3.14, el.GetDoubleOrDefault("pct"), 2);
    }

    #endregion

    #region GetBoolOrDefault

    [Fact]
    public void GetBoolOrDefault_True_ReturnsTrue()
    {
      var el = JsonHelper.ParseElement("""{"running":true}""");
      Assert.True(el.GetBoolOrDefault("running"));
    }

    [Fact]
    public void GetBoolOrDefault_False_ReturnsFalse()
    {
      var el = JsonHelper.ParseElement("""{"running":false}""");
      Assert.False(el.GetBoolOrDefault("running"));
    }

    [Fact]
    public void GetBoolOrDefault_StringTrue_ReturnsTrue()
    {
      var el = JsonHelper.ParseElement("""{"running":"true"}""");
      Assert.True(el.GetBoolOrDefault("running"));
    }

    [Fact]
    public void GetBoolOrDefault_Missing_ReturnsDefault()
    {
      var el = JsonHelper.ParseElement("""{}""");
      Assert.False(el.GetBoolOrDefault("running"));
      Assert.True(el.GetBoolOrDefault("running", true));
    }

    #endregion

    #region GetDateTimeOrDefault

    [Fact]
    public void GetDateTimeOrDefault_Iso8601_ParsesCorrectly()
    {
      var el = JsonHelper.ParseElement("""{"started":"2024-06-15T10:30:00Z"}""");
      var dt = el.GetDateTimeOrDefault("started");
      Assert.Equal(2024, dt.Year);
      Assert.Equal(6, dt.Month);
      Assert.Equal(15, dt.Day);
    }

    [Fact]
    public void GetDateTimeOrDefault_DockerFormat_ParsesCorrectly()
    {
      var el = JsonHelper.ParseElement("""{"created":"2024-01-15T08:30:00.123456789Z"}""");
      var dt = el.GetDateTimeOrDefault("created");
      Assert.Equal(2024, dt.Year);
    }

    [Fact]
    public void GetDateTimeOrDefault_Missing_ReturnsMinValue()
    {
      var el = JsonHelper.ParseElement("""{}""");
      Assert.Equal(DateTime.MinValue, el.GetDateTimeOrDefault("started"));
    }

    [Fact]
    public void GetDateTimeOrDefault_EmptyString_ReturnsMinValue()
    {
      var el = JsonHelper.ParseElement("""{"started":""}""");
      Assert.Equal(DateTime.MinValue, el.GetDateTimeOrDefault("started"));
    }

    [Fact]
    public void GetDateTimeOrDefault_ZonelessInput_AssumesUtc_NotHostLocal()
    {
      var el = JsonHelper.ParseElement("""{"started":"2024-06-15T10:30:00"}""");

      Assert.Equal(new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc),
          el.GetDateTimeOrDefault("started"));
    }

    #endregion

    #region GetDateTimeOffsetOrDefault

    [Fact]
    public void GetDateTimeOffsetOrDefault_ExplicitOffset_IsPreserved()
    {
      var el = JsonHelper.ParseElement("""{"created":"2024-01-02T03:04:05+02:00"}""");
      Assert.Equal(TimeSpan.FromHours(2), el.GetDateTimeOffsetOrDefault("created").Offset);
    }

    [Fact]
    public void GetDateTimeOffsetOrDefault_ZuluInput_HasZeroOffset()
    {
      var el = JsonHelper.ParseElement("""{"created":"2024-01-02T03:04:05Z"}""");
      Assert.Equal(TimeSpan.Zero, el.GetDateTimeOffsetOrDefault("created").Offset);
    }

    [Fact]
    public void GetDateTimeOffsetOrDefault_ZonelessInput_AssumesUtc_NotHostLocal()
    {
      // Without an explicit zone the offset must be UTC (deterministic), never the parsing host's local offset.
      var el = JsonHelper.ParseElement("""{"created":"2024-06-15T10:30:00"}""");
      Assert.Equal(TimeSpan.Zero, el.GetDateTimeOffsetOrDefault("created").Offset);
    }

    [Fact]
    public void GetDateTimeOffsetOrDefault_Missing_ReturnsMinValue()
    {
      var el = JsonHelper.ParseElement("""{}""");
      Assert.Equal(DateTimeOffset.MinValue, el.GetDateTimeOffsetOrDefault("created"));
    }

    #endregion

    #region GetStringArray

    [Fact]
    public void GetStringArray_ValidArray_ReturnsStrings()
    {
      var el = JsonHelper.ParseElement("""{"env":["A=1","B=2","C=3"]}""");
      var arr = el.GetStringArray("env");
      Assert.Equal(3, arr.Length);
      Assert.Equal("A=1", arr[0]);
      Assert.Equal("C=3", arr[2]);
    }

    [Fact]
    public void GetStringArray_EmptyArray_ReturnsEmpty()
    {
      var el = JsonHelper.ParseElement("""{"env":[]}""");
      Assert.Empty(el.GetStringArray("env"));
    }

    [Fact]
    public void GetStringArray_Missing_ReturnsEmpty()
    {
      var el = JsonHelper.ParseElement("""{}""");
      Assert.Empty(el.GetStringArray("env"));
    }

    [Fact]
    public void GetStringArray_NonArray_ReturnsEmpty()
    {
      var el = JsonHelper.ParseElement("""{"env":"single"}""");
      Assert.Empty(el.GetStringArray("env"));
    }

    #endregion

    #region GetStringOrArray

    [Fact]
    public void GetStringOrArray_String_ReturnsSingleElement()
    {
      var el = JsonHelper.ParseElement("""{"cmd":"/bin/bash"}""");
      var arr = el.GetStringOrArray("cmd");
      Assert.Single(arr);
      Assert.Equal("/bin/bash", arr[0]);
    }

    [Fact]
    public void GetStringOrArray_Array_ReturnsAll()
    {
      var el = JsonHelper.ParseElement("""{"cmd":["/bin/bash","-c","echo hi"]}""");
      var arr = el.GetStringOrArray("cmd");
      Assert.Equal(3, arr.Length);
    }

    [Fact]
    public void GetStringOrArray_Null_ReturnsEmpty()
    {
      var el = JsonHelper.ParseElement("""{"cmd":null}""");
      Assert.Empty(el.GetStringOrArray("cmd"));
    }

    [Fact]
    public void GetStringOrArray_Missing_ReturnsEmpty()
    {
      var el = JsonHelper.ParseElement("""{}""");
      Assert.Empty(el.GetStringOrArray("cmd"));
    }

    #endregion

    #region GetStringDictionary

    [Fact]
    public void GetStringDictionary_ValidObject_ReturnsDictionary()
    {
      var el = JsonHelper.ParseElement("""{"labels":{"app":"web","env":"prod"}}""");
      var dict = el.GetStringDictionary("labels");
      Assert.Equal(2, dict.Count);
      Assert.Equal("web", dict["app"]);
      Assert.Equal("prod", dict["env"]);
    }

    [Fact]
    public void GetStringDictionary_Missing_ReturnsEmptyDict()
    {
      var el = JsonHelper.ParseElement("""{}""");
      Assert.Empty(el.GetStringDictionary("labels"));
    }

    [Fact]
    public void GetStringDictionary_NonObject_ReturnsEmptyDict()
    {
      var el = JsonHelper.ParseElement("""{"labels":"flat"}""");
      Assert.Empty(el.GetStringDictionary("labels"));
    }

    #endregion

    private class TestDto
    {
      public string? Name { get; set; }
      public int Value { get; set; }
    }
  }
}
