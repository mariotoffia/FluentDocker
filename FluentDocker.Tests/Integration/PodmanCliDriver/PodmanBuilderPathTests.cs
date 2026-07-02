using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Services.Extensions;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Integration test for the DOCUMENTED consumer path (P4). The README promotes the fluent
  /// <c>Builder().WithinPodmanCli(...).UseContainer(...).BuildAsync()</c> path, yet the existing
  /// integration tests only exercise the drivers directly. This copies the README "Standard
  /// container (Podman CLI)" sample so the builder path — which nulls out empty collections before
  /// calling the driver (the P0 root cause) — is covered end-to-end.
  /// </summary>
  /// <remarks>
  /// Requires a live Podman machine. The base <see cref="PodmanDriverTestBase"/> auto-starts /
  /// creates one in <c>InitializeAsync</c>; if that is unavailable the whole fixture is skipped.
  /// </remarks>
  [Collection("PodmanDriver")]
  [Trait("Category", "PodmanIntegration")]
  public class PodmanBuilderPathTests : PodmanDriverTestBase
  {
    [Fact]
    public async Task Builder_WithinPodmanCli_UseContainer_ReadmeSample_Succeeds()
    {
      // Faithful copy of README §"Standard container (Podman CLI)", reusing the base-built kernel.
      await using var results = await new Builder()
          .WithinPodmanCli(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage(NginxImage)
              .ExposePort("80")
              .WaitForPort("80/tcp", 30000))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotEmpty(results.Containers);

      // Mirrors the README's ToHostExposedEndpoint("80/tcp"); WaitForPort already guaranteed the
      // port is mapped by the time BuildAsync returned.
      var endpoint = await results.Containers[0]
          .ToHostExposedEndpointAsync("80/tcp", TestContext.Current.CancellationToken);
      Assert.NotNull(endpoint);
      Assert.True(endpoint.Port > 0);
    }
  }
}
