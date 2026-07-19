using FluentDocker.Common;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public class ModelOperationGateNormalizationTests
  {
    [Fact]
    public void KeyFor_DefaultDockerHubAlias_UsesSameGateKey()
    {
      var defaultHub = ModelReference.Parse("docker.io/ai/qwen3");
      var shortName = ModelReference.Parse("ai/qwen3");

      Assert.Equal("ai/qwen3:latest", ModelOperationGate.KeyFor(defaultHub));
      Assert.Equal(ModelOperationGate.KeyFor(shortName), ModelOperationGate.KeyFor(defaultHub));
    }
  }
}
