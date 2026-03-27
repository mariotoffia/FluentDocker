using FluentDocker.Common;
using FluentDocker.Extensions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class OsExtensionsTests
  {
    [Fact]
    public void ToMsysPath_OnNonWindows_ReturnsSamePath()
    {
      // Arrange
      var path = @"C:\Users\test\folder";

      // Act
      var result = path.ToMsysPath();

      // Assert - on macOS/Linux the method returns the input unchanged
      if (!FdOs.IsWindows())
        Assert.Equal(path, result);
    }

    [Fact]
    public void ToMsysPath_OnNonWindows_UnixPath_ReturnsSamePath()
    {
      // Arrange
      var path = "/usr/local/bin";

      // Act
      var result = path.ToMsysPath();

      // Assert - Unix paths are returned unchanged on non-Windows
      if (!FdOs.IsWindows())
        Assert.Equal("/usr/local/bin", result);
    }

    [Fact]
    public void ToMsysPath_OnNonWindows_EmptyString_ReturnsSamePath()
    {
      // Arrange
      var path = "";

      // Act
      var result = path.ToMsysPath();

      // Assert - on non-Windows, empty string is returned unchanged.
      // NOTE: On Windows this would throw IndexOutOfRangeException because
      // the method accesses path[0] and path.Substring(2) without checking
      // the string length. This is a potential production bug.
      if (!FdOs.IsWindows())
        Assert.Equal("", result);
    }

    [Fact(Skip = "Requires Windows")]
    public void ToMsysPath_WindowsDrivePath_ConvertsToDriveFormat()
    {
      // Arrange
      var path = @"C:\Users\test";

      // Act
      var result = path.ToMsysPath();

      // Assert - on Windows, "C:\Users\test" becomes "//c/Users/test"
      Assert.Equal("//c/Users/test", result);
    }

    [Fact(Skip = "Requires Windows")]
    public void ToMsysPath_WindowsBackslashes_ConvertedToForward()
    {
      // Arrange
      var path = @"D:\some\deep\nested\path";

      // Act
      var result = path.ToMsysPath();

      // Assert - all backslashes after the drive letter are converted to forward slashes
      Assert.Equal("//d/some/deep/nested/path", result);
      Assert.DoesNotContain("\\", result);
    }

    [Fact(Skip = "Requires Windows")]
    public void ToMsysPath_WindowsDriveLetter_Lowercased()
    {
      // Arrange
      var path = @"E:\Folder";

      // Act
      var result = path.ToMsysPath();

      // Assert - the drive letter is lowercased: "E:" becomes "//e"
      Assert.StartsWith("//e", result);
    }
  }
}
