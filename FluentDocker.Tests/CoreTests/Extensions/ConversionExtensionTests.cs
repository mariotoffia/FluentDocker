using System.Numerics;
using FluentDocker.Extensions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class ConversionExtensionTests
  {
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void NullOrEmptyString_ReturnsMinimumValue(string? input)
    {
      var num = input!.Convert();
      Assert.Equal(long.MinValue, num);
    }

    [Theory]
    [InlineData("2googles")]
    [InlineData("42p")]
    [InlineData("wrongFormat42")]
    [InlineData("-3498lfk")]
    public void InvalidUnitInput_ReturnsMinimumValue(string input)
    {
      var num = input.Convert();
      Assert.Equal(long.MinValue, num);
    }

    [Fact]
    public void LessThanLongMinimumValue_ReturnsMinimumValue()
    {
      var lessThanMinimum = (new BigInteger(long.MinValue)) - 1;
      var input = lessThanMinimum.ToString() + "g";

      var num = input.Convert();
      Assert.Equal(long.MinValue, num);
    }

    [Fact]
    public void GreaterThanLongMaximumValue_ReturnsMinimumValue()
    {
      var greaterThanMaximum = (new BigInteger(long.MaxValue)) + 1;
      var input = greaterThanMaximum.ToString() + "g";

      var num = input.Convert();
      Assert.Equal(long.MinValue, num);
    }

    [Fact]
    public void ValidByteInput_ReturnsExactNumber()
    {
      var input = "42b";

      var num = input.Convert();
      Assert.Equal(42, num);
    }

    [Fact]
    public void ValidKilobyteInput_ReturnsCorrectKilobyteNumber()
    {
      var input = "42k";

      var num = input.Convert();
      Assert.Equal(43008, num); // 42 * 1024
    }

    [Fact]
    public void ValidMegabyteInput_ReturnsCorrectMegabyteNumber()
    {
      var input = "42m";

      var num = input.Convert();
      Assert.Equal(44040192, num); // 42 * 1024 * 1024
    }

    [Fact]
    public void ValidGigabyteInput_ReturnsCorrectGigabyteNumber()
    {
      var input = "42g";

      var num = input.Convert();
      Assert.Equal(45097156608, num); // 42 * 1024 * 1024 * 1024
    }

    [Fact]
    public void CustomUnit_WorksWhenInAllowedList()
    {
      // When 'm' is in the allowed list, it should work
      var input = "10m";

      var num = input.Convert("m", "g");
      Assert.Equal(10 * 1024 * 1024, num);
    }

    [Fact]
    public void CustomUnit_FailsWhenNotInAllowedList()
    {
      // When 'k' is NOT in the allowed list, it should fail
      var input = "10k";

      var num = input.Convert("m", "g"); // only m and g allowed
      Assert.Equal(long.MinValue, num);
    }

    [Fact]
    public void NoUnit_ReturnsMinValue()
    {
      // Plain number without unit should return MinValue as invalid format
      var input = "100";

      var num = input.Convert();
      Assert.Equal(long.MinValue, num);
    }

    [Fact]
    public void UnknownUnit_ReturnsMinValue()
    {
      // Unknown unit should return MinValue
      var input = "100x";

      var num = input.Convert();
      Assert.Equal(long.MinValue, num);
    }
  }
}

