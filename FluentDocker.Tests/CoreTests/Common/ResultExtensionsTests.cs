using System;
using System.Collections.Generic;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public class ResultExtensionsTests
  {
    // ──────────────────────────────────────────────
    //  ToSuccess tests
    // ──────────────────────────────────────────────

    [Fact]
    public void ToSuccess_SetsIsSuccessTrue()
    {
      // Act
      var result = 42.ToSuccess();

      // Assert
      Assert.True(result.IsSuccess);
      Assert.False(result.IsFailure);
      Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ToSuccess_WithLog_SetsLog()
    {
      // Act
      var result = "data".ToSuccess("some log");

      // Assert
      Assert.True(result.IsSuccess);
      Assert.Equal("some log", result.Log);
    }

    [Fact]
    public void ToSuccess_WithNullLog_SetsEmptyLog()
    {
      // Act
      var result = "data".ToSuccess((string)null!);

      // Assert
      Assert.True(result.IsSuccess);
      Assert.Equal(string.Empty, result.Log);
    }

    [Fact]
    public void ToSuccess_WithLogList_JoinsEntries()
    {
      // Arrange
      IList<string> log = ["line1", "line2", "line3"];

      // Act
      var result = 99.ToSuccess(log);

      // Assert
      Assert.True(result.IsSuccess);
      var expected = string.Join(Environment.NewLine, "line1", "line2", "line3");
      Assert.Equal(expected, result.Log);
      Assert.Equal(string.Empty, result.Error);
    }

    // ──────────────────────────────────────────────
    //  ToFailure tests
    // ──────────────────────────────────────────────

    [Fact]
    public void ToFailure_SetsIsSuccessFalse()
    {
      // Act
      var result = "bad".ToFailure<string>("error occurred");

      // Assert
      Assert.False(result.IsSuccess);
      Assert.True(result.IsFailure);
    }

    [Fact]
    public void ToFailure_SetsErrorMessage()
    {
      // Act
      var result = 0.ToFailure("something went wrong");

      // Assert
      Assert.Equal("something went wrong", result.Error);
      Assert.Equal(0, result.Value);
    }

    [Fact]
    public void ToFailure_WithLog_SetsLog()
    {
      // Act
      var result = 0.ToFailure("err", "log output");

      // Assert
      Assert.True(result.IsFailure);
      Assert.Equal("err", result.Error);
      Assert.Equal("log output", result.Log);
    }

    [Fact]
    public void ToFailure_WithLogList_JoinsEntries()
    {
      // Arrange
      IList<string> log = ["step1", "step2"];

      // Act
      var result = (-1).ToFailure("failed", log);

      // Assert
      Assert.True(result.IsFailure);
      Assert.Equal("failed", result.Error);
      var expected = string.Join(Environment.NewLine, "step1", "step2");
      Assert.Equal(expected, result.Log);
    }

    // ──────────────────────────────────────────────
    //  FromLog tests
    // ──────────────────────────────────────────────

    [Fact]
    public void FromLog_NullList_ReturnsEmpty()
    {
      // Arrange
      IList<string>? entries = null;

      // Act
      var result = entries!.FromLog();

      // Assert
      Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FromLog_EmptyList_ReturnsEmpty()
    {
      // Arrange
      IList<string> entries = [];

      // Act
      var result = entries.FromLog();

      // Assert
      Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FromLog_MultipleEntries_JoinsWithNewline()
    {
      // Arrange
      IList<string> entries = ["alpha", "beta", "gamma"];

      // Act
      var result = entries.FromLog();

      // Assert
      var expected = string.Join(Environment.NewLine, "alpha", "beta", "gamma");
      Assert.Equal(expected, result);
    }

    // ──────────────────────────────────────────────
    //  ToEntires tests (note: misspelled in source)
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ToEntires_NullOrEmpty_ReturnsEmptyArray(string? log)
    {
      // Act
      var result = log!.ToEntires();

      // Assert
      Assert.NotNull(result);
      Assert.Empty(result);
    }

    [Fact]
    public void ToEntires_MultipleLines_SplitsCorrectly()
    {
      // Arrange — mix \n and \r\n to verify both separators
      var log = "first\nsecond\r\nthird";

      // Act
      var result = log.ToEntires();

      // Assert
      Assert.Equal(3, result.Length);
      Assert.Equal("first", result[0]);
      Assert.Equal("second", result[1]);
      Assert.Equal("third", result[2]);
    }
  }
}
