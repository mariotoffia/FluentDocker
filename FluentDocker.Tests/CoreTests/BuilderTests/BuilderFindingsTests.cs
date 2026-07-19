using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for the fail-fast guards added across the builders:
  /// BLDR-1 (model config without ForModel), BLDR-3 (negative cleanup timeout),
  /// BLDR-5 (compose environment/env-file guards) and BLDR-6 (null builder receiver).
  /// </summary>
  [Trait("Category", "Unit")]
  public sealed class BuilderFindingsTests
  {
    // ---- BLDR-1: build-time model config requires ForModel ----

    [Fact]
    public async Task ModelRunner_WithContextSizeButNoForModel_ThrowsAtBuild()
    {
      var pack = new MockDriverPack().SetupModelChat("ok").EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var runnerBuilder = new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .WithContextSize(2048);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runnerBuilder.BuildAsync(TestContext.Current.CancellationToken));

        Assert.Contains("ForModel", ex.Message);
      }
    }

    [Fact]
    public async Task ModelRunner_PullIfMissingButNoForModel_ThrowsAtBuild()
    {
      var pack = new MockDriverPack().SetupModelChat("ok").EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var runnerBuilder = new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .PullIfMissing();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runnerBuilder.BuildAsync(TestContext.Current.CancellationToken));

        Assert.Contains("ForModel", ex.Message);
      }
    }

    [Fact]
    public async Task ModelRunner_NoForModelAndNoConfig_BuildsWithoutThrowing()
    {
      // Sanity: the guard fires only when discardable config is present, not for a bare runner.
      var pack = new MockDriverPack().SetupModelChat("ok").EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        await using var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(runner);
      }
    }

    // ---- BLDR-3: negative cleanup timeout rejected at entry ----

    [Fact]
    public async Task BuildAsync_NegativeCleanupTimeout_ThrowsAtEntry()
    {
      var pack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var builder = new Builder().WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("alpine"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            builder.BuildAsync(TimeSpan.FromSeconds(-1), TestContext.Current.CancellationToken));
      }
    }

    // ---- BLDR-5: compose environment / env-file guards ----

    [Fact]
    public async Task ComposeWithEnvironment_NullDictionary_ThrowsAtConfigurationTime()
    {
      var pack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        Assert.Throws<ArgumentNullException>(() =>
            new Builder().WithinDriver("docker", kernel)
                .UseCompose(c => c.WithEnvironment((IDictionary<string, string>)null!)));
      }
    }

    [Fact]
    public async Task ComposeWithEnvFile_BlankPath_ThrowsAtConfigurationTime()
    {
      var pack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        Assert.ThrowsAny<ArgumentException>(() =>
            new Builder().WithinDriver("docker", kernel)
                .UseCompose(c => c.WithEnvFile("   ")));
      }
    }

    // ---- BLDR-6: public extension methods reject a null receiver ----

    [Fact]
    public void RequireDriver_NullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            ((IDriverScopedBuilder)null!).RequireDriver<IContainerDriver>());

    [Fact]
    public void TryDriver_NullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            ((IDriverScopedBuilder)null!).TryDriver<IContainerDriver>());

    [Fact]
    public void UseModelRunner_NullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            ((IDriverScopedBuilder)null!).UseModelRunner());

    [Fact]
    public void TryUseModelRunner_NullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            ((IDriverScopedBuilder)null!).TryUseModelRunner(out _));

    [Fact]
    public void UseModel_NullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            ((IDriverScopedBuilder)null!).UseModel("ai/smollm2"));
  }
}
