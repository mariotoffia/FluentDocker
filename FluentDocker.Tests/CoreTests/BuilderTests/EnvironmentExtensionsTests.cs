using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public sealed class EnvironmentExtensionsTests
  {
    [Fact]
    public void WrapValue_BackslashTerminatedValue_EscapesBackslashBeforeWrapping()
    {
      var result = new TemplateString[] { @"PATH=C:\temp\" }.WrapValue();

      Assert.Equal(@"PATH=""C:\\temp\\""", Assert.Single(result));
    }

    [Fact]
    public void WrapValue_EmbeddedQuote_EscapesOnce()
    {
      var result = new TemplateString[] { "QUOTE=a\"b" }.WrapValue();

      Assert.Equal(@"QUOTE=""a\""b""", Assert.Single(result));
    }

    [Fact]
    public void WrapValue_AlreadyWrappedSimpleValue_DoesNotDoubleWrap()
    {
      var result = new TemplateString[] { @"NAME=""hello""" }.WrapValue();

      Assert.Equal(@"NAME=""hello""", Assert.Single(result));
    }

    [Fact]
    public void WrapValue_EscapedTrailingQuote_IsTreatedAsLiteralValue()
    {
      var result = new TemplateString[] { "TRICKY=\"a\\\"" }.WrapValue();

      Assert.Equal("TRICKY=\"\\\"a\\\\\\\"\"", Assert.Single(result));
    }

    [Theory]
    [InlineData("=foo")]
    [InlineData(" =foo")]
    public void WrapValue_EmptyOrWhitespaceName_ThrowsFluentDockerException(string input)
    {
      var ex = Assert.Throws<FluentDockerException>(() => new TemplateString[] { input }.WrapValue());

      Assert.Contains("Expected format name=value", ex.Message);
    }

    [Fact]
    public void WrapValue_MissingEquals_StillThrowsFluentDockerException()
    {
      var ex = Assert.Throws<FluentDockerException>(() => new TemplateString[] { "missing" }.WrapValue());

      Assert.Contains("missing equal sign", ex.Message);
    }

    // BF-11: quotes count as a wrap only when balanced around the WHOLE value. Here the interior
    // contains unescaped quotes ("x" y="z"), so the value is literal and must survive unchanged
    // (previously the outer quotes were stripped, altering the value).
    [Fact]
    public void WrapValue_InteriorUnescapedQuotes_TreatsValueAsLiteral()
    {
      var result = new TemplateString[] { "V=\"x\" y=\"z\"" }.WrapValue();

      // Escaped whole-value wrap: "\"x\" y=\"z\"" decodes back to the original "x" y="z".
      Assert.Equal("V=\"\\\"x\\\" y=\\\"z\\\"\"", Assert.Single(result));
    }

    // CE-7: a balanced pre-wrapped value with escaped interior quotes must unescape on unwrap so
    // re-wrapping is lossless: wrap(unwrap(x)) == x (previously FOO="a\"b" re-escaped to "a\\\"b").
    [Fact]
    public void WrapValue_PreWrappedWithEscapedQuote_RoundTripsUnchanged()
    {
      var result = new TemplateString[] { "FOO=\"a\\\"b\"" }.WrapValue();

      Assert.Equal("FOO=\"a\\\"b\"", Assert.Single(result));
    }

    [Fact]
    public void WrapValue_PreWrappedWithEscapedBackslash_RoundTripsUnchanged()
    {
      var result = new TemplateString[] { "P=\"C:\\\\temp\"" }.WrapValue();

      Assert.Equal("P=\"C:\\\\temp\"", Assert.Single(result));
    }

    [Fact]
    public void WrapValue_IsIdempotentForItsOwnOutput()
    {
      var once = new TemplateString[] { "QUOTE=a\"b" }.WrapValue();
      var twice = new TemplateString[] { Assert.Single(once) }.WrapValue();

      Assert.Equal(Assert.Single(once), Assert.Single(twice));
    }
  }
}
