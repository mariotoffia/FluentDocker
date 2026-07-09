#nullable enable
using System;

namespace FluentDocker.Extensions
{
  /// <summary>
  /// Numeric comparison helpers.
  /// </summary>
  public static class ComparisonExtensions
  {
    /// <summary>
    /// Determines whether two doubles differ by less than the allowed delta.
    /// </summary>
    /// <param name="initialValue">The first value.</param>
    /// <param name="value">The second value.</param>
    /// <param name="maximumDifferenceAllowed">The exclusive maximum allowed difference.</param>
    /// <returns><c>true</c> when the values are approximately equal.</returns>
    public static bool IsApproximatelyEqualTo(this double initialValue, double value,
      double maximumDifferenceAllowed = 0.00001d)
    {
      return Math.Abs(initialValue - value) < maximumDifferenceAllowed;
    }

    /// <summary>
    /// Determines whether two floats differ by less than the allowed delta.
    /// </summary>
    /// <param name="initialValue">The first value.</param>
    /// <param name="value">The second value.</param>
    /// <param name="maximumDifferenceAllowed">The exclusive maximum allowed difference.</param>
    /// <returns><c>true</c> when the values are approximately equal.</returns>
    public static bool IsApproximatelyEqualTo(this float initialValue, float value,
      float maximumDifferenceAllowed = 0.00001f)
    {
      return Math.Abs(initialValue - value) < maximumDifferenceAllowed;
    }
  }
}
