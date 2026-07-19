using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public class CommandLineQuotingTests
  {
    [Fact]
    public void QuoteArgumentIfNeeded_TrailingBackslashBeforeClosingQuote_DoublesOnlyTrailingBackslash()
    {
      var result = CommandLineQuoting.QuoteArgumentIfNeeded(@"C:\My Files\");

      Assert.Equal("\"C:\\My Files\\\\\"", result);
    }

    [Fact]
    public void QuoteArgumentIfNeeded_EmbeddedQuote_EscapesQuote()
    {
      var result = CommandLineQuoting.QuoteArgumentIfNeeded("say \"hi\"");

      Assert.Equal("\"say \\\"hi\\\"\"", result);
    }

    [Fact]
    public void QuoteArgumentIfNeeded_Newline_QuotesArgument()
    {
      var result = CommandLineQuoting.QuoteArgumentIfNeeded("line1\nline2");

      Assert.Equal("\"line1\nline2\"", result);
    }

    [Fact]
    public void QuoteArgumentIfNeeded_WindowsPathWithSpaces_DoesNotDoubleInteriorBackslashes()
    {
      var result = CommandLineQuoting.QuoteArgumentIfNeeded(@"C:\My Files\x");

      Assert.Equal("\"C:\\My Files\\x\"", result);
    }
  }
}
