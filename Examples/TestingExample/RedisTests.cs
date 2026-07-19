using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Testing.Xunit;
using Xunit;

namespace TestingExample
{
  public sealed class RedisFixture : XunitContainerFixtureBase
  {
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
      builder
        .UseImage("redis:7-alpine")
        .WithName($"fluentdocker-testing-example-{Guid.NewGuid():N}")
        .WaitForPort("6379/tcp");
    }
  }

  [Trait("Category", "Integration")]
  public sealed class RedisTests : IClassFixture<RedisFixture>
  {
    private readonly RedisFixture fixture;

    public RedisTests(RedisFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task Redis_IsRunning()
    {
      var inspected = await fixture.Container.InspectAsync(TestContext.Current.CancellationToken);
      Assert.NotNull(inspected.State);
      Assert.True(inspected.State.Running);
    }
  }
}
