#pragma warning disable CS0618
using FluentDocker.Model.Stacks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class StackLsResponseTests
  {
    [Fact]
    public void ToOrchestrator_KnownValues_AreCaseInsensitive()
    {
      Assert.Equal(Orchestrator.Swarm, StackLsResponse.ToOrchestrator("SWARM"));
      Assert.Equal(Orchestrator.Kubernetes, StackLsResponse.ToOrchestrator("Kubernetes"));
    }

    [Fact]
    public void ToOrchestrator_UnknownValue_ReturnsUnknown()
    {
      Assert.Equal(Orchestrator.Unknown, StackLsResponse.ToOrchestrator("future-runtime"));
    }
  }
}
