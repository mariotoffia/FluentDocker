using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.MsTest;
using FluentDocker.Testing.Xunit;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class FixtureAdapterBugFixTests
  {
    [Fact]
    public async Task XunitContainerFixtureBase_SecondInitializeThrows()
    {
      var fixture = new TestXunitContainerFixture();

      await fixture.InitializeAsync();

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => fixture.InitializeAsync().AsTask());

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task MsTestClassFixture_WritesWarningWhenClassCleanupIsSkipped()
    {
      var fixture = new LeakyMsTestClassFixture();
      await fixture.TestInitializeAsync();

      using var writer = new StringWriter();
      LeakyMsTestClassFixture.WriteWarnings(writer);

      Assert.Contains(nameof(LeakyMsTestClassFixture), writer.ToString());

      await LeakyMsTestClassFixture.CleanupAsync();
    }

    [Fact]
    public async Task MsTestClassFixture_DoesNotWarnAfterClassCleanup()
    {
      var fixture = new CleanMsTestClassFixture();
      await fixture.TestInitializeAsync();
      await CleanMsTestClassFixture.CleanupAsync();

      using var writer = new StringWriter();
      CleanMsTestClassFixture.WriteWarnings(writer);

      Assert.DoesNotContain(nameof(CleanMsTestClassFixture), writer.ToString());
    }

    [Fact]
    public async Task XunitFixture_IsDockerAvailableAsync_ProbesConfiguredDriver()
    {
      // Fixture uses a Podman driver; the probe must honor it, not hard-code "docker-cli".
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("podman-cli");
      mockPack.SystemDriver
          .Setup(d => d.PingAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var fixture = new PodmanProbeFixture(() => Task.FromResult(kernel));

      Assert.True(await fixture.IsDockerAvailableAsync(TestContext.Current.CancellationToken));
    }

    private sealed class TestXunitContainerFixture : XunitContainerFixtureBase
    {
      private FluentDockerKernel? _kernel;

      protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
          async () =>
          {
            var pack = new MockDriverPack();
            pack
                .SetupContainerCreate()
                .SetupContainerStart()
                .SetupContainerInspect(running: true)
                .SetupContainerStop()
                .SetupContainerRemove();
            _kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack)
                .ConfigureAwait(false);
            return _kernel;
          };

      protected override DockerResourceOptions? GetOptions() =>
          new() { Driver = DriverSelection.Specific("docker") };

      protected override void ConfigureContainer(IContainerBuilder builder)
      {
        builder.UseImage("alpine:latest");
      }
    }

    private sealed class PodmanProbeFixture : XunitContainerFixtureBase
    {
      private readonly Func<Task<FluentDockerKernel>> _factory;

      public PodmanProbeFixture(Func<Task<FluentDockerKernel>> factory) => _factory = factory;

      protected override Func<Task<FluentDockerKernel>>? KernelFactory => _factory;

      protected override DockerResourceOptions? GetOptions() =>
          new() { Driver = DriverSelection.Specific("podman-cli") };

      protected override void ConfigureContainer(IContainerBuilder builder) { }
    }

    private abstract class TestMsTestClassFixture<TFixture>
        : MsTestClassContainerFixtureBase<TFixture>
        where TFixture : TestMsTestClassFixture<TFixture>
    {
      protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
          async () =>
          {
            var pack = new MockDriverPack();
            pack
                .SetupContainerCreate()
                .SetupContainerStart()
                .SetupContainerInspect(running: true)
                .SetupContainerStop()
                .SetupContainerRemove();
            return await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack)
                .ConfigureAwait(false);
          };

      protected override DockerResourceOptions? GetOptions() =>
          new() { Driver = DriverSelection.Specific("docker") };

      protected override void ConfigureContainer(IContainerBuilder builder)
      {
        builder.UseImage("alpine:latest");
      }
    }

    private sealed class LeakyMsTestClassFixture : TestMsTestClassFixture<LeakyMsTestClassFixture>
    {
      public static Task CleanupAsync() => CleanupClassAsync();

      public static void WriteWarnings(TextWriter writer) => WriteUncleanedFixtureWarnings(writer);
    }

    private sealed class CleanMsTestClassFixture : TestMsTestClassFixture<CleanMsTestClassFixture>
    {
      public static Task CleanupAsync() => CleanupClassAsync();

      public static void WriteWarnings(TextWriter writer) => WriteUncleanedFixtureWarnings(writer);
    }
  }
}
