using System;
using System.Globalization;
using System.Threading;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public class JsonElementProductionReadinessTests
  {
    [Fact]
    public void GetDoubleOrDefault_StringDecimal_ParsesWithInvariantCulture()
    {
      var originalCulture = CultureInfo.CurrentCulture;
      Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
      try
      {
        var el = JsonHelper.ParseElement("""{"pct":"3.14"}""");

        Assert.Equal(3.14, el.GetDoubleOrDefault("pct"), 2);
      }
      finally
      {
        Thread.CurrentThread.CurrentCulture = originalCulture;
      }
    }

    [Fact]
    public void GetDateTimeOrDefault_WithOffset_ReturnsUtcInstant()
    {
      var el = JsonHelper.ParseElement("""{"created":"2024-01-15T10:30:00+02:00"}""");

      var dt = el.GetDateTimeOrDefault("created");

      Assert.Equal(DateTimeKind.Utc, dt.Kind);
      Assert.Equal(new DateTime(2024, 1, 15, 8, 30, 0, DateTimeKind.Utc), dt);
    }

    [Fact]
    public void GetStringArray_NonStringScalars_CoercesValues()
    {
      var el = JsonHelper.ParseElement("""{"env":["A=1",42,true,null]}""");

      Assert.Equal(["A=1", "42", "True", ""], el.GetStringArray("env"));
    }

    [Fact]
    public void GetStringOrArray_NonStringScalars_CoercesValues()
    {
      var el = JsonHelper.ParseElement("""{"cmd":[42,false]}""");

      Assert.Equal(["42", "False"], el.GetStringOrArray("cmd"));
    }

    [Fact]
    public void GetStringDictionary_NonStringScalars_CoercesValues()
    {
      var el = JsonHelper.ParseElement("""{"labels":{"retries":3,"enabled":true,"empty":null}}""");

      var dict = el.GetStringDictionary("labels");

      Assert.Equal("3", dict["retries"]);
      Assert.Equal("True", dict["enabled"]);
      Assert.Equal(string.Empty, dict["empty"]);
    }
  }
}
