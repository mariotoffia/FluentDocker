using System;
using System.Collections.Generic;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  /// <summary>
  /// Unit tests for <see cref="Option{T}"/>, <see cref="Result{T}"/>,
  /// and <see cref="ResultExtensions"/>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class OptionAndResultTests
  {
    // ──────────────────────────────────────────────
    //  Option<T> tests
    // ──────────────────────────────────────────────

    [Fact]
    public void Option_ConstructorWithNonNull_HasValueTrueAndValueSet()
    {
      // Arrange
      var inner = "hello";

      // Act
      var option = new Option<string>(inner);

      // Assert
      Assert.True(option.HasValue);
      Assert.Equal("hello", option.Value);
    }

    [Fact]
    public void Option_ConstructorWithNull_HasValueFalseAndValueNull()
    {
      // Arrange / Act
      var option = new Option<string>(null!);

      // Assert
      Assert.False(option.HasValue);
      Assert.Null(option.Value);
    }

    [Fact]
    public void Option_ExplicitOperatorT_ReturnsInnerValue()
    {
      // Arrange
      var option = new Option<string>("world");

      // Act
      var value = (string)option!;

      // Assert
      Assert.Equal("world", value);
    }

    [Fact]
    public void Option_ExplicitOperatorT_NoneOption_ReturnsNull()
    {
      // Arrange
      var option = new Option<string>(null!);

      // Act
      var value = (string?)option;

      // Assert
      Assert.Null(value);
    }

    [Fact]
    public void Option_ExplicitOperator_WrapsValueCorrectly()
    {
      // Arrange
      var inner = "wrapped";

      // Act
      var option = (Option<string>)inner;

      // Assert
      Assert.True(option.HasValue);
      Assert.Equal("wrapped", option.Value);
    }

    [Fact]
    public void Option_ExplicitOperator_NullValue_ProducesNoneOption()
    {
      // Arrange
      string? inner = null;

      // Act
      var option = (Option<string>)inner!;

      // Assert
      Assert.False(option.HasValue);
      Assert.Null(option.Value);
    }

    [Fact]
    public void Option_RoundTrip_ExplicitWrapThenExplicitUnwrap()
    {
      // Arrange
      var original = "roundtrip";

      // Act
      var option = (Option<string>)original;
      var unwrapped = (string)option;

      // Assert
      Assert.Equal(original, unwrapped);
    }

    // ──────────────────────────────────────────────
    //  Result<T> via ToSuccess tests
    // ──────────────────────────────────────────────

    [Fact]
    public void ToSuccess_DefaultLog_IsSuccessAndEmptyErrorAndEmptyLog()
    {
      // Arrange / Act
      var result = 42.ToSuccess();

      // Assert
      Assert.True(result.IsSuccess);
      Assert.False(result.IsFailure);
      Assert.Equal(42, result.Value);
      Assert.Equal(string.Empty, result.Log);
      Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void ToSuccess_WithLogString_SetsLogCorrectly()
    {
      // Arrange / Act
      var result = "data".ToSuccess("some log output");

      // Assert
      Assert.True(result.IsSuccess);
      Assert.Equal("data", result.Value);
      Assert.Equal("some log output", result.Log);
      Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void ToSuccess_WithNullLogString_SetsLogToEmpty()
    {
      // Arrange / Act
      var result = "data".ToSuccess((string)null!);

      // Assert
      Assert.True(result.IsSuccess);
      Assert.Equal(string.Empty, result.Log);
    }

    [Fact]
    public void ToSuccess_WithListLog_JoinsLinesWithNewline()
    {
      // Arrange
      IList<string> log = new List<string> { "line1", "line2", "line3" };

      // Act
      var result = 99.ToSuccess(log);

      // Assert
      Assert.True(result.IsSuccess);
      Assert.Equal(99, result.Value);
      var expected = string.Join(Environment.NewLine, "line1", "line2", "line3");
      Assert.Equal(expected, result.Log);
      Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void ToSuccess_WithEmptyListLog_SetsLogToEmpty()
    {
      // Arrange
      IList<string> log = new List<string>();

      // Act
      var result = 1.ToSuccess(log);

      // Assert
      Assert.Equal(string.Empty, result.Log);
    }

    // ──────────────────────────────────────────────
    //  Result<T> via ToFailure tests
    // ──────────────────────────────────────────────

    [Fact]
    public void ToFailure_WithError_IsFailureAndErrorSet()
    {
      // Arrange / Act
      var result = "bad".ToFailure<string>("something went wrong");

      // Assert
      Assert.False(result.IsSuccess);
      Assert.True(result.IsFailure);
      Assert.Equal("bad", result.Value);
      Assert.Equal("something went wrong", result.Error);
      Assert.Equal(string.Empty, result.Log);
    }

    [Fact]
    public void ToFailure_WithErrorAndLogString_BothSet()
    {
      // Arrange / Act
      var result = 0.ToFailure("err msg", "log output");

      // Assert
      Assert.True(result.IsFailure);
      Assert.Equal(0, result.Value);
      Assert.Equal("err msg", result.Error);
      Assert.Equal("log output", result.Log);
    }

    [Fact]
    public void ToFailure_WithErrorAndNullLog_SetsLogToEmpty()
    {
      // Arrange / Act
      var result = 0.ToFailure("err", (string)null!);

      // Assert
      Assert.True(result.IsFailure);
      Assert.Equal("err", result.Error);
      Assert.Equal(string.Empty, result.Log);
    }

    [Fact]
    public void ToFailure_WithErrorAndListLog_JoinsAndSetsLog()
    {
      // Arrange
      IList<string> log = new List<string> { "step1", "step2" };

      // Act
      var result = (-1).ToFailure("failed", log);

      // Assert
      Assert.True(result.IsFailure);
      Assert.Equal((-1), result.Value);
      Assert.Equal("failed", result.Error);
      var expected = string.Join(Environment.NewLine, "step1", "step2");
      Assert.Equal(expected, result.Log);
    }

    [Fact]
    public void ToFailure_WithErrorAndEmptyListLog_SetsLogToEmpty()
    {
      // Arrange
      IList<string> log = new List<string>();

      // Act
      var result = 0.ToFailure("oops", log);

      // Assert
      Assert.Equal(string.Empty, result.Log);
      Assert.Equal("oops", result.Error);
    }

    [Fact]
    public void Result_IsSuccessAndIsFailure_AreMutuallyExclusive()
    {
      // Arrange / Act
      var success = "ok".ToSuccess();
      var failure = "nope".ToFailure<string>("error");

      // Assert
      Assert.True(success.IsSuccess);
      Assert.False(success.IsFailure);
      Assert.False(failure.IsSuccess);
      Assert.True(failure.IsFailure);
    }

    // ──────────────────────────────────────────────
    //  ResultExtensions.FromLog tests
    // ──────────────────────────────────────────────

    [Fact]
    public void FromLog_NullList_ReturnsEmptyString()
    {
      // Arrange
      IList<string>? entries = null;

      // Act
      var result = entries!.FromLog();

      // Assert
      Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FromLog_EmptyList_ReturnsEmptyString()
    {
      // Arrange
      IList<string> entries = new List<string>();

      // Act
      var result = entries.FromLog();

      // Assert
      Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FromLog_SingleEntry_ReturnsThatEntry()
    {
      // Arrange
      IList<string> entries = new List<string> { "only line" };

      // Act
      var result = entries.FromLog();

      // Assert
      Assert.Equal("only line", result);
    }

    [Fact]
    public void FromLog_MultipleEntries_ReturnsNewlineJoined()
    {
      // Arrange
      IList<string> entries = new List<string> { "alpha", "beta", "gamma" };

      // Act
      var result = entries.FromLog();

      // Assert
      var expected = string.Join(Environment.NewLine, "alpha", "beta", "gamma");
      Assert.Equal(expected, result);
    }

    // ──────────────────────────────────────────────
    //  ResultExtensions.ToEntires tests (note: misspelled in source)
    // ──────────────────────────────────────────────

    [Fact]
    public void ToEntires_NullString_ReturnsEmptyArray()
    {
      // Arrange
      string? log = null;

      // Act
      var result = log!.ToEntires();

      // Assert
      Assert.NotNull(result);
      Assert.Empty(result);
    }

    [Fact]
    public void ToEntires_EmptyString_ReturnsEmptyArray()
    {
      // Arrange / Act
      var result = string.Empty.ToEntires();

      // Assert
      Assert.NotNull(result);
      Assert.Empty(result);
    }

    [Fact]
    public void ToEntires_SingleLine_ReturnsSingleElement()
    {
      // Arrange / Act
      var result = "one line".ToEntires();

      // Assert
      Assert.Single(result);
      Assert.Equal("one line", result[0]);
    }

    [Fact]
    public void ToEntires_NewlineSeparated_SplitsCorrectly()
    {
      // Arrange
      var log = "line1\nline2\nline3";

      // Act
      var result = log.ToEntires();

      // Assert
      Assert.Equal(3, result.Length);
      Assert.Equal("line1", result[0]);
      Assert.Equal("line2", result[1]);
      Assert.Equal("line3", result[2]);
    }

    [Fact]
    public void ToEntires_CrLfSeparated_SplitsCorrectly()
    {
      // Arrange
      var log = "first\r\nsecond\r\nthird";

      // Act
      var result = log.ToEntires();

      // Assert
      Assert.Equal(3, result.Length);
      Assert.Equal("first", result[0]);
      Assert.Equal("second", result[1]);
      Assert.Equal("third", result[2]);
    }

    [Fact]
    public void ToEntires_MixedLineEndings_SplitsCorrectly()
    {
      // Arrange
      var log = "a\nb\r\nc";

      // Act
      var result = log.ToEntires();

      // Assert
      Assert.Equal(3, result.Length);
      Assert.Equal("a", result[0]);
      Assert.Equal("b", result[1]);
      Assert.Equal("c", result[2]);
    }

    [Fact]
    public void ToEntires_RemovesEmptyEntries()
    {
      // Arrange — trailing newline produces an empty trailing entry
      var log = "x\n\ny";

      // Act
      var result = log.ToEntires();

      // Assert — RemoveEmptyEntries drops the blank between x and y
      Assert.Equal(2, result.Length);
      Assert.Equal("x", result[0]);
      Assert.Equal("y", result[1]);
    }

    // ──────────────────────────────────────────────
    //  FromLog + ToEntires round-trip
    // ──────────────────────────────────────────────

    [Fact]
    public void FromLog_ThenToEntires_RoundTripsCorrectly()
    {
      // Arrange
      IList<string> original = new List<string> { "one", "two", "three" };

      // Act
      var joined = original.FromLog();
      var split = joined.ToEntires();

      // Assert
      Assert.Equal(3, split.Length);
      Assert.Equal("one", split[0]);
      Assert.Equal("two", split[1]);
      Assert.Equal("three", split[2]);
    }
  }
}
