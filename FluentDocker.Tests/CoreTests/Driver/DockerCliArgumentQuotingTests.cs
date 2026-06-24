using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Binary;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Tests for <see cref="DockerCliDriverBase"/> argument quoting. The execution path
  /// uses the string <c>ProcessStartInfo.Arguments</c> (not <c>ArgumentList</c>), so an
  /// argument containing whitespace/control characters must be quoted or it would split
  /// into multiple tokens. <c>QuoteArgumentIfNeeded</c> is <c>protected static</c>, so a
  /// tiny test subclass surfaces it (same pattern as sibling tests).
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliArgumentQuotingTests
  {
    private sealed class QuotingDriver : DockerCliDriverBase
    {
      public QuotingDriver(IBinaryResolver resolver) : base(resolver)
      {
      }

      public static string Quote(string argument) => QuoteArgumentIfNeeded(argument);
    }

    private static string Quote(string argument)
    {
      // A real driver instance is not required to reach the static helper, but constructing
      // one mirrors how the protected member is used in production and keeps the subclass live.
      var resolver = new Mock<IBinaryResolver>();
      _ = new QuotingDriver(resolver.Object);
      return QuotingDriver.Quote(argument);
    }

    [Fact]
    public void Empty_IsQuotedAsEmptyPair()
    {
      Assert.Equal("\"\"", Quote(string.Empty));
    }

    [Fact]
    public void Plain_IsReturnedUnchanged()
    {
      Assert.Equal("plain", Quote("plain"));
    }

    [Fact]
    public void Space_IsQuoted()
    {
      // Regression guard: space was already covered by ShellMetaCharacters.
      Assert.Equal("\"a b\"", Quote("a b"));
    }

    [Theory]
    [InlineData("a\nb")]   // newline
    [InlineData("a\rb")]   // carriage return
    [InlineData("a\tb")]   // tab
    [InlineData("a\vb")]   // vertical tab
    [InlineData("a\fb")]   // form feed
    public void WhitespaceOrControl_IsQuoted(string argument)
    {
      var result = Quote(argument);

      // Must be wrapped in double quotes so it stays a single argument token.
      Assert.StartsWith("\"", result);
      Assert.EndsWith("\"", result);
      Assert.NotEqual(argument, result);
    }

    [Theory]
    [InlineData("$(whoami)")]
    [InlineData("`id`")]
    public void ShellMetacharacters_AreQuoted(string argument)
    {
      var result = Quote(argument);

      Assert.StartsWith("\"", result);
      Assert.EndsWith("\"", result);
    }

    [Fact]
    public void EmbeddedQuoteAndBackslash_AreEscaped()
    {
      // A bare backslash is not a metacharacter/whitespace/control char, so the value is
      // not quoted and is returned verbatim (escaping only runs once quoting is triggered).
      Assert.Equal("a\\b", Quote("a\\b"));

      // An embedded double-quote forces quoting; the quote is escaped: a"b -> "a\"b".
      Assert.Equal("\"a\\\"b\"", Quote("a\"b"));

      // Once quoting is triggered (here by a space), embedded backslashes are doubled:
      // a\b c -> "a\\b c".
      Assert.Equal("\"a\\\\b c\"", Quote("a\\b c"));
    }
  }
}
