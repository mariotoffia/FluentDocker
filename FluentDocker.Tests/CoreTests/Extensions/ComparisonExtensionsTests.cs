using FluentDocker.Extensions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class ComparisonExtensionsTests
  {
    // ---------------------------------------------------------------
    // Double overload
    // ---------------------------------------------------------------

    [Fact]
    public void IsApproximatelyEqualToDouble_SameValues_ReturnsTrue()
    {
      // Arrange
      const double value = 1.0;

      // Act
      var result = value.IsApproximatelyEqualTo(1.0);

      // Assert
      Assert.True(result);
    }

    [Theory]
    [InlineData(1.0, 1.000009, true)]
    [InlineData(1.0, 1.000005, true)]
    [InlineData(1.0, 0.999995, true)]
    public void IsApproximatelyEqualToDouble_WithinDefaultTolerance_ReturnsTrue(
      double initial, double value, bool expected)
    {
      // Act
      var result = initial.IsApproximatelyEqualTo(value);

      // Assert
      Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1.0, 1.0001)]
    [InlineData(1.0, 0.9999)]
    [InlineData(0.0, 0.001)]
    public void IsApproximatelyEqualToDouble_OutsideDefaultTolerance_ReturnsFalse(
      double initial, double value)
    {
      // Act
      var result = initial.IsApproximatelyEqualTo(value);

      // Assert
      Assert.False(result);
    }

    [Theory]
    [InlineData(1.0, 1.05, 0.1, true)]
    [InlineData(1.0, 1.15, 0.1, false)]
    [InlineData(5.0, 5.5, 1.0, true)]
    public void IsApproximatelyEqualToDouble_CustomTolerance_ReturnsExpected(
      double initial, double value, double tolerance, bool expected)
    {
      // Act
      var result = initial.IsApproximatelyEqualTo(value, tolerance);

      // Assert
      Assert.Equal(expected, result);
    }

    [Fact]
    public void IsApproximatelyEqualToDouble_ExactlyAtBoundary_ReturnsFalse()
    {
      // Arrange - difference is exactly equal to tolerance (strict < means false)
      const double initial = 1.0;
      const double value = 1.5;
      const double tolerance = 0.5;

      // Act
      var result = initial.IsApproximatelyEqualTo(value, tolerance);

      // Assert - Math.Abs(1.0 - 1.5) = 0.5, 0.5 < 0.5 is false
      Assert.False(result);
    }

    [Fact]
    public void IsApproximatelyEqualToDouble_NaN_ReturnsFalse()
    {
      // Arrange - Math.Abs(NaN - x) = NaN, NaN < tolerance = false
      const double nan = double.NaN;

      // Act
      var result = nan.IsApproximatelyEqualTo(1.0);

      // Assert
      Assert.False(result);
    }

    [Theory]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, double.NegativeInfinity)]
    public void IsApproximatelyEqualToDouble_SameInfinity_ReturnsFalse(
      double initial, double value)
    {
      // Arrange - Infinity - Infinity = NaN, so NaN < tolerance = false
      // Act
      var result = initial.IsApproximatelyEqualTo(value);

      // Assert
      Assert.False(result);
    }

    // ---------------------------------------------------------------
    // Float overload
    // ---------------------------------------------------------------

    [Fact]
    public void IsApproximatelyEqualToFloat_SameValues_ReturnsTrue()
    {
      // Arrange
      const float value = 1.0f;

      // Act
      var result = value.IsApproximatelyEqualTo(1.0f);

      // Assert
      Assert.True(result);
    }

    [Theory]
    [InlineData(1.0f, 1.000009f, true)]
    [InlineData(1.0f, 1.000005f, true)]
    public void IsApproximatelyEqualToFloat_WithinDefaultTolerance_ReturnsTrue(
      float initial, float value, bool expected)
    {
      // Act
      var result = initial.IsApproximatelyEqualTo(value);

      // Assert
      Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1.0f, 1.001f)]
    [InlineData(1.0f, 0.999f)]
    public void IsApproximatelyEqualToFloat_OutsideDefaultTolerance_ReturnsFalse(
      float initial, float value)
    {
      // Act
      var result = initial.IsApproximatelyEqualTo(value);

      // Assert
      Assert.False(result);
    }

    [Theory]
    [InlineData(1.0f, 1.05f, 0.1f, true)]
    [InlineData(1.0f, 1.15f, 0.1f, false)]
    public void IsApproximatelyEqualToFloat_CustomTolerance_ReturnsExpected(
      float initial, float value, float tolerance, bool expected)
    {
      // Act
      var result = initial.IsApproximatelyEqualTo(value, tolerance);

      // Assert
      Assert.Equal(expected, result);
    }

    [Fact]
    public void IsApproximatelyEqualToFloat_NaN_ReturnsFalse()
    {
      // Arrange
      const float nan = float.NaN;

      // Act
      var result = nan.IsApproximatelyEqualTo(1.0f);

      // Assert
      Assert.False(result);
    }

    [Fact]
    public void IsApproximatelyEqualToFloat_VerySmallDifference_ReturnsTrue()
    {
      // Arrange
      const float initial = 0.0f;
      const float value = 0.000001f;

      // Act
      var result = initial.IsApproximatelyEqualTo(value);

      // Assert - 0.000001 < 0.00001 is true
      Assert.True(result);
    }
  }
}
