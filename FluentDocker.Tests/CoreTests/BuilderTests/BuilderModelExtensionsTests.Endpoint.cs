using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// MR3: when no per-runner endpoint is supplied, the built runner must report the endpoint the
  /// pack/context is actually bound to (<c>DriverContext.ModelRunnerEndpoint</c>) — not the
  /// hardcoded localhost:12434 default — so status/diagnostics match where inference really goes.
  /// </summary>
  public partial class BuilderModelExtensionsTests
  {
    [Fact]
    public async Task RunnerBuilder_NoExplicitEndpoint_ReportsContextEndpoint_NotLocalhostDefault()
    {
      var endpoint = ModelRunnerEndpoint.HostTcp(9911); // non-default port
      var pack = new MockDriverPack().EnableModelDrivers();
      var context = new DriverContext("docker") { ModelRunnerEndpoint = endpoint };
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack, context);

      await using (kernel)
      {
        await using var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel("ai/smollm2")
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(endpoint.BaseAddress, runner.Endpoint);
        Assert.NotEqual(ModelRunnerEndpoint.HostTcp().BaseAddress, runner.Endpoint); // not localhost:12434
      }
    }

    [Fact]
    public async Task RunnerBuilder_ExplicitEndpoint_StillWins_OverContextEndpoint()
    {
      // Precedence guard: an explicitly supplied endpoint must still take priority over the context.
      var contextEndpoint = ModelRunnerEndpoint.HostTcp(9911);
      var explicitEndpoint = ModelRunnerEndpoint.HostTcp(9922);
      var pack = new MockDriverPack().EnableModelDrivers();
      var context = new DriverContext("docker") { ModelRunnerEndpoint = contextEndpoint };
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack, context);

      await using (kernel)
      {
        await using var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel("ai/smollm2")
            .WithEndpoint(explicitEndpoint)
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(explicitEndpoint.BaseAddress, runner.Endpoint);
      }
    }

    [Fact]
    public async Task RunnerBuilder_WithEndpointThenInferenceDriver_ThrowsConflict()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);

      await using (kernel)
      {
        var builder = new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .WithEndpoint(ModelRunnerEndpoint.HostTcp(9922));

        Assert.Throws<InvalidOperationException>(() =>
            builder.WithInferenceDriver(new Mock<IModelInferenceDriver>().Object));
      }
    }

    [Fact]
    public async Task RunnerBuilder_WithInferenceDriverThenEndpoint_ThrowsConflict()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);

      await using (kernel)
      {
        var builder = new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .WithInferenceDriver(new Mock<IModelInferenceDriver>().Object);

        Assert.Throws<InvalidOperationException>(() =>
            builder.WithEndpoint(ModelRunnerEndpoint.HostTcp(9922)));
      }
    }
  }
}
