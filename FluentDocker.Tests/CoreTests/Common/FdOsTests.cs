using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public class FdOsTests
  {
    [Fact]
    public void IsWindows_ReturnsBool()
    {
      // Act — should not throw
      var result = FdOs.IsWindows();

      // Assert
      Assert.IsType<bool>(result);
    }

    [Fact]
    public void IsOsx_ReturnsBool()
    {
      // Act — should not throw
      var result = FdOs.IsOsx();

      // Assert
      Assert.IsType<bool>(result);
    }

    [Fact]
    public void IsLinux_ReturnsBool()
    {
      // Act — should not throw
      var result = FdOs.IsLinux();

      // Assert
      Assert.IsType<bool>(result);
    }

    [Fact]
    public void ExactlyOnePlatformIsTrue()
    {
      // Act
      var trueCount = 0;
      if (FdOs.IsWindows())
        trueCount++;
      if (FdOs.IsOsx())
        trueCount++;
      if (FdOs.IsLinux())
        trueCount++;

      // Assert — exactly one of the three must be true
      Assert.Equal(1, trueCount);
    }
  }
}
