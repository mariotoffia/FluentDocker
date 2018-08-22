using System;

namespace Ductus.FluentDocker.Extensions
{
  public static class ComparisonExtensions
  {
    public static bool IsApproximatelyEqualTo(this double initialValue, double value,
      double maximumDifferenceAllowed = 0.00001d)
    {
      return Math.Abs(initialValue - value) < maximumDifferenceAllowed;
    }

    public static bool IsApproximatelyEqualTo(this float initialValue, float value,
      float maximumDifferenceAllowed = 0.00001f)
    {
      return Math.Abs(initialValue - value) < maximumDifferenceAllowed;
    }
  }
}