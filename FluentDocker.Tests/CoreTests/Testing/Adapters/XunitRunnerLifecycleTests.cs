using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing.Adapters
{
  /// <summary>
  /// Verifies that xUnit's runner actually drives <see cref="IAsyncLifetime"/>
  /// on fixtures injected via <c>IClassFixture&lt;T&gt;</c>.
  /// </summary>

  #region Fixture definitions

  /// <summary>
  /// Abstract base fixture: xUnit calls InitializeAsync/DisposeAsync automatically.
  /// </summary>
  public class MockContainerRunnerFixture : XunitContainerFixtureBase
  {
    protected override void ConfigureContainer(IContainerBuilder b)
        => b.UseImage("alpine:latest");

    protected override Func<Task<FluentDockerKernel>> KernelFactory
        => CreateMockKernelAsync;

    private static async Task<FluentDockerKernel> CreateMockKernelAsync()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      mockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();
      return kernel;
    }
  }

  /// <summary>
  /// Concrete fixture with <see cref="XunitContainerFixture.Configure"/>:
  /// xUnit calls <see cref="IAsyncLifetime.InitializeAsync"/> which uses stored config.
  /// </summary>
  public class ConfiguredContainerRunnerFixture : XunitContainerFixture
  {
    public ConfiguredContainerRunnerFixture() => Configure(
          c => c.UseImage("alpine:latest"),
          kernelFactory: CreateMockKernelAsync);

    private static async Task<FluentDockerKernel> CreateMockKernelAsync()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      mockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();
      return kernel;
    }
  }

  #endregion

  #region Runner-level tests using IClassFixture

  /// <summary>
  /// Tests that xUnit runner invokes <see cref="IAsyncLifetime"/> on
  /// <see cref="XunitContainerFixtureBase"/> subclass via <c>IClassFixture</c>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class XunitRunnerAbstractFixtureTests : IClassFixture<MockContainerRunnerFixture>
  {
    private readonly MockContainerRunnerFixture _fixture;

    public XunitRunnerAbstractFixtureTests(MockContainerRunnerFixture fixture)
        => _fixture = fixture;

    [Fact]
    public void Resource_IsInitialized()
        => Assert.True(_fixture.Resource.IsInitialized);

    [Fact]
    public void Container_IsAvailable()
        => Assert.NotNull(_fixture.Container);

    [Fact]
    public void Kernel_IsAvailable()
        => Assert.NotNull(_fixture.Kernel);
  }

  /// <summary>
  /// Tests that xUnit runner invokes <see cref="IAsyncLifetime"/> on
  /// <see cref="XunitContainerFixture"/> with <see cref="XunitContainerFixture.Configure"/>
  /// via <c>IClassFixture</c>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class XunitRunnerConfiguredFixtureTests : IClassFixture<ConfiguredContainerRunnerFixture>
  {
    private readonly ConfiguredContainerRunnerFixture _fixture;

    public XunitRunnerConfiguredFixtureTests(ConfiguredContainerRunnerFixture fixture)
        => _fixture = fixture;

    [Fact]
    public void Resource_IsInitialized()
        => Assert.True(_fixture.Resource.IsInitialized);

    [Fact]
    public void Container_IsAvailable()
        => Assert.NotNull(_fixture.Container);

    [Fact]
    public void Kernel_IsAvailable()
        => Assert.NotNull(_fixture.Kernel);
  }

  #endregion
}
