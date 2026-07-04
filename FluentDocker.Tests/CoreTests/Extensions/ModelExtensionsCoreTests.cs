using FluentDocker.Extensions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class ModelExtensionsCoreTests
  {
    [Fact]
    public void ToPlainId_Null_ReturnsNull()
    {
      string? id = null;

      Assert.Null(id!.ToPlainId());
    }

    [Fact]
    public void ToPlainId_AlgorithmPrefixed_ReturnsPlainId()
    {
      Assert.Equal("abcdef", "sha256:abcdef".ToPlainId());
    }
  }
}
