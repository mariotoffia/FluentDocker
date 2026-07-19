using System.Globalization;
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
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
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
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void InvalidUnitInput_ReturnsMinimumValue(string input)
    {
      var num = input.Convert();
      Assert.Equal(long.MinValue, num);
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void LessThanLongMinimumValue_ReturnsMinimumValue()
    {
      var lessThanMinimum = (new BigInteger(long.MinValue)) - 1;
      var input = lessThanMinimum.ToString(CultureInfo.InvariantCulture) + "g";

      var num = input.Convert();
      Assert.Equal(long.MinValue, num);
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void GreaterThanLongMaximumValue_ReturnsMinimumValue()
    {
      var greaterThanMaximum = (new BigInteger(long.MaxValue)) + 1;
      var input = greaterThanMaximum.ToString(CultureInfo.InvariantCulture) + "g";

      var num = input.Convert();
      Assert.Equal(long.MinValue, num);
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void DecimalMultiplyOverflow_ReturnsMinimumValue()
    {
      var num = "8000000000000000000000000000k".Convert();

      Assert.Equal(long.MinValue, num);
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void ValidByteInput_ReturnsExactNumber()
    {
      var input = "42b";

      var num = input.Convert();
      Assert.Equal(42, num);
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void ValidKilobyteInput_ReturnsCorrectKilobyteNumber()
    {
      var input = "42k";

      var num = input.Convert();
      Assert.Equal(43008, num); // 42 * 1024
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void ValidMegabyteInput_ReturnsCorrectMegabyteNumber()
    {
      var input = "42m";

      var num = input.Convert();
      Assert.Equal(44040192, num); // 42 * 1024 * 1024
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void ValidGigabyteInput_ReturnsCorrectGigabyteNumber()
    {
      var input = "42g";

      var num = input.Convert();
      Assert.Equal(45097156608, num); // 42 * 1024 * 1024 * 1024
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void CustomUnit_WorksWhenInAllowedList()
    {
      // When 'm' is in the allowed list, it should work
      var input = "10m";

      var num = input.Convert("m", "g");
      Assert.Equal(10 * 1024 * 1024, num);
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void CustomUnit_FailsWhenNotInAllowedList()
    {
      // When 'k' is NOT in the allowed list, it should fail
      var input = "10k";

      var num = input.Convert("m", "g"); // only m and g allowed
      Assert.Equal(long.MinValue, num);
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void NoUnit_ReturnsBytes()
    {
      var input = "100";

      var num = input.Convert();
      Assert.Equal(100, num);
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void UnknownUnit_ReturnsMinValue()
    {
      // Unknown unit should return MinValue
      var input = "100x";

      var num = input.Convert();
      Assert.Equal(long.MinValue, num);
    }

    [Theory]
    [InlineData("1.5g", 1610612736L)]
    [InlineData("1024", 1024L)]
    [InlineData("1G", 1073741824L)]
    [InlineData("512m", 536870912L)]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void Convert_ValidModernInputs_ReturnsBytes(string input, long expected)
    {
      var num = input.Convert();

      Assert.Equal(expected, num);
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void Convert_Garbage_ReturnsMinimumValue()
    {
      var num = "garbage".Convert();

      Assert.Equal(long.MinValue, num);
    }
  }
}
