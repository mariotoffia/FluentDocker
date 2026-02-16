using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Integration tests for Podman network driver.
  /// Requires Podman to be installed.
  /// </summary>
  [Collection("PodmanDriver")]
  [Trait("Category", "PodmanIntegration")]
  public class PodmanNetworkDriverTests : PodmanDriverTestBase
  {
    [Fact]
    public async Task CreateAndRemove_Succeeds()
    {
      var name = UniqueName("net");
      try
      {
        var config = new NetworkCreateConfig { Name = name };
        var createResult = await NetworkDriver.CreateAsync(Context, config, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(createResult.Success, $"Create failed: {createResult.Error}");
        Assert.NotNull(createResult.Data.Id);
      }
      finally
      {
        await NetworkDriver.RemoveAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);
      }
    }

    [Fact]
    public async Task ListNetworks_ReturnsResults()
    {
      var result = await NetworkDriver.ListAsync(Context, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success, $"List failed: {result.Error}");
      // Podman always has at least the default "podman" network
      Assert.NotEmpty(result.Data);
    }

    [Fact]
    public async Task InspectNetwork_ReturnsDetails()
    {
      var name = UniqueName("net");
      try
      {
        await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig { Name = name }, cancellationToken: TestContext.Current.CancellationToken);

        var result = await NetworkDriver.InspectAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"Inspect failed: {result.Error}");
        Assert.NotNull(result.Data.Name);
      }
      finally
      {
        await NetworkDriver.RemoveAsync(Context, name, cancellationToken: TestContext.Current.CancellationToken);
      }
    }
  }
}
