using FluentDocker.Extensions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class StringExtensionsTests
  {
    [Fact]
    public void WrapWithChar_NotWrapped_WrapsWithChar()
    {
      // Arrange
      var input = "hello";

      // Act
      var result = input.WrapWithChar("'");

      // Assert
      Assert.Equal("'hello'", result);
    }

    [Fact]
    public void WrapWithChar_AlreadyWrapped_DoesNotDoubleWrap()
    {
      // Arrange
      var input = "'hello'";

      // Act
      var result = input.WrapWithChar("'");

      // Assert
      Assert.Equal("'hello'", result);
    }

    [Fact]
    public void WrapWithChar_OnlyStartWrapped_WrapsEnd()
    {
      // Arrange
      var input = "'hello";

      // Act
      var result = input.WrapWithChar("'");

      // Assert
      Assert.Equal("'hello'", result);
    }

    [Fact]
    public void WrapWithChar_OnlyEndWrapped_WrapsStart()
    {
      // Arrange
      var input = "hello'";

      // Act
      var result = input.WrapWithChar("'");

      // Assert
      Assert.Equal("'hello'", result);
    }

    [Fact]
    public void WrapWithChar_EmptyString_WrapsEmpty()
    {
      // Arrange
      var input = "";

      // Act
      var result = input.WrapWithChar("'");

      // Assert
      // After prepending "'" to "", we get "'", which already ends with "'"
      // so the method does not append again, resulting in just "'"
      Assert.Equal("'", result);
    }

    [Fact]
    public void WrapWithChar_DoubleQuote_WrapsCorrectly()
    {
      // Arrange
      var input = "hello";

      // Act
      var result = input.WrapWithChar("\"");

      // Assert
      Assert.Equal("\"hello\"", result);
    }

    [Fact]
    public void WrapWithChar_MultiCharWrapper_Works()
    {
      // Arrange
      var input = "hello";

      // Act
      var result = input.WrapWithChar("##");

      // Assert
      Assert.Equal("##hello##", result);
    }

    [Fact]
    public void WrapWithChar_SingleChar_Works()
    {
      // Arrange
      var input = "test";

      // Act
      var result = input.WrapWithChar("X");

      // Assert
      Assert.Equal("XtestX", result);
    }
  }
}
