using System;
using System.Reflection;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Drivers.Podman;
using FluentDocker.Model.Drivers;
using Xunit;

#pragma warning disable CS0618 // DriverComponent obsolete — intentional usage

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// Unit tests for DockerApiDriverPack: resolution, capabilities, and SysCtl wiring.
  [Trait("Category", "Unit")]
  public class DockerApiDriverPackTests : IAsyncDisposable
  {
    private readonly DockerApiDriverPack _pack = new();

    private static DriverContext CreateContext() =>
        new("docker-api", "tcp://localhost:2375");

    private async Task InitializePackAsync() =>
        await _pack.InitializeAsync(CreateContext());

    public async ValueTask DisposeAsync()
    {
      GC.SuppressFinalize(this);
      await _pack.DisposeAsync();
    }

    // ── Basic properties ────────────────────────────────────────────

    [Fact]
    public void Type_ReturnsDockerApi() =>
        Assert.Equal(DriverType.DockerApi, _pack.Type);

    [Fact]
    public void Runtime_ReturnsDocker() =>
        Assert.Equal(RuntimeType.Docker, _pack.Runtime);

    // ── Capabilities ────────────────────────────────────────────────

    [Fact]
    public async Task GetCapabilities_ReturnsCorrectValues()
    {
      var caps = await _pack.GetCapabilitiesAsync(TestContext.Current.CancellationToken);
      Assert.True(caps.SupportsContainers);
      Assert.True(caps.SupportsImages);
      Assert.True(caps.SupportsNetworks);
      Assert.True(caps.SupportsVolumes);
      Assert.True(caps.SupportsSystem);
      Assert.False(caps.SupportsCompose);
      Assert.False(caps.SupportsPods);
      Assert.False(caps.SupportsKubernetes);
      Assert.False(caps.SupportsMachines);
      Assert.False(caps.SupportsManifests);
    }

    // ── Pre-initialization guards ───────────────────────────────────

    [Fact]
    public void SysCtlGeneric_BeforeInit_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => _pack.SysCtl<IContainerDriver>("docker-api"));

    [Fact]
    public void SysCtlByComponent_BeforeInit_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => _pack.SysCtl("docker-api", DriverComponent.Container));

    [Fact]
    public void SysCtlByType_BeforeInit_Throws()
    {
#pragma warning disable CA2263 // Intentionally testing the non-generic SysCtl(string, Type) overload
      Assert.Throws<InvalidOperationException>(
          () => _pack.SysCtl("docker-api", typeof(IContainerDriver)));
#pragma warning restore CA2263
    }

    [Fact]
    public void TrySysCtl_BeforeInit_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => _pack.TrySysCtl<IContainerDriver>("docker-api", out _));

    [Fact]
    public void TryResolve_BeforeInit_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => _pack.TryResolve(typeof(IContainerDriver), out _));

    [Fact]
    public void GetSupportedInterfaces_BeforeInit_Throws() =>
        Assert.Throws<InvalidOperationException>(() => _pack.GetSupportedInterfaces());

    [Fact]
    public async Task IsHealthy_BeforeInit_ReturnsFalse() =>
        Assert.False(await _pack.IsHealthyAsync(TestContext.Current.CancellationToken));

    // ── Post-init: SysCtl resolves all 8 interfaces by Type ─────────

    [Theory]
    [InlineData(typeof(IContainerDriver))]
    [InlineData(typeof(IImageDriver))]
    [InlineData(typeof(INetworkDriver))]
    [InlineData(typeof(IVolumeDriver))]
    [InlineData(typeof(ISystemDriver))]
    [InlineData(typeof(IAuthDriver))]
    [InlineData(typeof(IStreamDriver))]
    [InlineData(typeof(IServiceDriver))]
    public async Task SysCtlByType_AfterInit_ResolvesInterface(Type interfaceType)
    {
      await InitializePackAsync();
      var result = _pack.SysCtl("docker-api", interfaceType);
      Assert.NotNull(result);
      Assert.True(interfaceType.IsInstanceOfType(result));
    }

    // ── Post-init: SysCtl<T> generic resolves correct driver ────────

    [Fact]
    public async Task SysCtlGeneric_AfterInit_ReturnsCorrectDriverTypes()
    {
      await InitializePackAsync();
      Assert.NotNull(_pack.SysCtl<IContainerDriver>("docker-api"));
      Assert.NotNull(_pack.SysCtl<IImageDriver>("docker-api"));
      Assert.NotNull(_pack.SysCtl<IVolumeDriver>("docker-api"));
      Assert.NotNull(_pack.SysCtl<IStreamDriver>("docker-api"));
    }

    [Fact]
    public async Task SysCtlGeneric_UnsupportedType_ThrowsInterfaceNotSupported()
    {
      await InitializePackAsync();
      Assert.Throws<InterfaceNotSupportedException>(
          () => _pack.SysCtl<IPodmanPodDriver>("docker-api"));
    }

    // ── Post-init: TrySysCtl<T> ────────────────────────────────────

    [Fact]
    public async Task TrySysCtl_Supported_ReturnsTrueWithInstance()
    {
      await InitializePackAsync();
      var found = _pack.TrySysCtl<IContainerDriver>("docker-api", out var driver);
      Assert.True(found);
      Assert.NotNull(driver);
    }

    [Fact]
    public async Task TrySysCtl_Unsupported_ReturnsFalseWithNull()
    {
      await InitializePackAsync();
      var found = _pack.TrySysCtl<IPodmanPodDriver>("docker-api", out var driver);
      Assert.False(found);
      Assert.Null(driver);
    }

    // ── Post-init: TryResolve ───────────────────────────────────────

    [Fact]
    public async Task TryResolve_Supported_ReturnsTrueWithInstance()
    {
      await InitializePackAsync();
      var found = _pack.TryResolve(typeof(INetworkDriver), out var impl);
      Assert.True(found);
      Assert.IsAssignableFrom<INetworkDriver>(impl);
    }

    [Fact]
    public async Task TryResolve_Unsupported_ReturnsFalse()
    {
      await InitializePackAsync();
      var found = _pack.TryResolve(typeof(IPodmanPodDriver), out var impl);
      Assert.False(found);
      Assert.Null(impl);
    }

    // ── Post-init: GetSupportedInterfaces ───────────────────────────

    [Fact]
    public async Task GetSupportedInterfaces_Returns8Types()
    {
      await InitializePackAsync();
      var ifaces = _pack.GetSupportedInterfaces();
      Assert.Equal(8, ifaces.Count);
      Assert.Contains(typeof(IContainerDriver), ifaces);
      Assert.Contains(typeof(IImageDriver), ifaces);
      Assert.Contains(typeof(INetworkDriver), ifaces);
      Assert.Contains(typeof(IVolumeDriver), ifaces);
      Assert.Contains(typeof(ISystemDriver), ifaces);
      Assert.Contains(typeof(IAuthDriver), ifaces);
      Assert.Contains(typeof(IStreamDriver), ifaces);
      Assert.Contains(typeof(IServiceDriver), ifaces);
    }

    // ── Post-init: SysCtl by DriverComponent ────────────────────────

    [Theory]
    [InlineData(DriverComponent.Container)]
    [InlineData(DriverComponent.Image)]
    [InlineData(DriverComponent.Network)]
    [InlineData(DriverComponent.Volume)]
    [InlineData(DriverComponent.System)]
    public async Task SysCtlByComponent_AfterInit_Resolves(DriverComponent component)
    {
      await InitializePackAsync();
      Assert.NotNull(_pack.SysCtl("docker-api", component));
    }

    [Fact]
    public async Task SysCtlByComponent_Compose_ThrowsArgumentException()
    {
      await InitializePackAsync();
      Assert.Throws<ArgumentException>(
          () => _pack.SysCtl("docker-api", DriverComponent.Compose));
    }

    // ── IsHealthyAsync delegates to connection PingAsync ────────────

    [Fact]
    public async Task IsHealthy_PingTrue_ReturnsTrue()
    {
      var mock = new MockDockerApiConnection().SetupPing(true);
      await using var pack = new DockerApiDriverPack();
      await pack.InitializeAsync(CreateContext(), TestContext.Current.CancellationToken);
      InjectConnection(pack, mock);

      Assert.True(await pack.IsHealthyAsync(TestContext.Current.CancellationToken));
      var requests = mock.GetRequests();
      Assert.Single(requests);
      Assert.Equal("PING", requests[0].Method);
    }

    [Fact]
    public async Task IsHealthy_PingFalse_ReturnsFalse()
    {
      var mock = new MockDockerApiConnection().SetupPing(false);
      await using var pack = new DockerApiDriverPack();
      await pack.InitializeAsync(CreateContext(), TestContext.Current.CancellationToken);
      InjectConnection(pack, mock);

      Assert.False(await pack.IsHealthyAsync(TestContext.Current.CancellationToken));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void InjectConnection(
        DockerApiDriverPack pack, MockDockerApiConnection mock)
    {
      var field = typeof(DockerApiDriverPack)
          .GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance);
      Assert.NotNull(field);
      field!.SetValue(pack, mock);
    }
  }
}
