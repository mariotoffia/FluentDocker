using FluentDocker.Extensions;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class EnvironmentExtensionsTests
  {
    [Fact]
    public void WrapValue_EmptyValue_ProducesQuotedEmptyValue()
    {
      TemplateString[] input = ["NAME="];

      var result = input.WrapValue();

      Assert.Equal(["NAME=\"\""], result);
    }

    [Fact]
    public void WrapValue_EmbeddedQuote_EscapesQuote()
    {
      TemplateString[] input = ["NAME=a\"b"];

      var result = input.WrapValue();

      Assert.Equal(["NAME=\"a\\\"b\""], result);
    }

    [Fact]
    public void WrapValue_SingleCharValue_WrapsValue()
    {
      TemplateString[] input = ["NAME=x"];

      var result = input.WrapValue();

      Assert.Equal(["NAME=\"x\""], result);
    }
  }
}
