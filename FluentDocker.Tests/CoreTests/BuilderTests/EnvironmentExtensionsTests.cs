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
  }
}
