using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

#pragma warning disable CS0618 // DriverComponent obsolete -- intentional usage

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliDriverPack interface resolution, capabilities,
  /// and pre/post-initialization behavior.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliDriverPackTests
  {
    #region Properties

    [Fact]
    public void Type_ReturnsDockerCli()
    {
      var pack = new DockerCliDriverPack();
      Assert.Equal(DriverType.DockerCli, pack.Type);
    }

    [Fact]
    public void Runtime_ReturnsDocker()
    {
      var pack = new DockerCliDriverPack();
      Assert.Equal(RuntimeType.Docker, pack.Runtime);
    }

    #endregion

    #region Capabilities

    [Fact]
    public async Task GetCapabilities_SupportsContainersImagesNetworksVolumesComposeSystem()
    {
      var pack = new DockerCliDriverPack();
      var caps = await pack.GetCapabilitiesAsync(TestContext.Current.CancellationToken);

      Assert.True(caps.SupportsContainers);
      Assert.True(caps.SupportsImages);
      Assert.True(caps.SupportsNetworks);
      Assert.True(caps.SupportsVolumes);
      Assert.True(caps.SupportsCompose);
      Assert.True(caps.SupportsSystem);
    }

    [Fact]
    public async Task GetCapabilities_DoesNotSupportPods()
    {
      var pack = new DockerCliDriverPack();
      var caps = await pack.GetCapabilitiesAsync(TestContext.Current.CancellationToken);

      Assert.False(caps.SupportsPods);
    }

    #endregion

    #region Before Initialization -- SysCtl Throws

    [Fact]
    public void SysCtl_Generic_BeforeInitialize_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() =>
          pack.SysCtl<IContainerDriver>("docker"));
    }

    [Fact]
    public void TrySysCtl_BeforeInitialize_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() =>
          pack.TrySysCtl<IContainerDriver>("docker", out _));
    }

    [Fact]
    public void SysCtlByComponent_BeforeInitialize_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() =>
          pack.SysCtl("docker", DriverComponent.Container));
    }

    [Fact]
    public void SysCtlByType_BeforeInitialize_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
#pragma warning disable CA2263
      Assert.Throws<InvalidOperationException>(() =>
          pack.SysCtl("docker", typeof(IContainerDriver)));
#pragma warning restore CA2263
    }

    #endregion

    #region Before Initialization -- TryResolve / GetSupportedInterfaces Throw

    [Fact]
    public void TryResolve_BeforeInitialize_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() =>
          pack.TryResolve(typeof(IContainerDriver), out _));
    }

    [Fact]
    public void GetSupportedInterfaces_BeforeInitialize_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() =>
          pack.GetSupportedInterfaces());
    }

    #endregion

    #region Before Initialization -- IsHealthy Returns False

    [Fact]
    public async Task IsHealthy_BeforeInitialize_ReturnsFalse()
    {
      var pack = new DockerCliDriverPack();
      var healthy = await pack.IsHealthyAsync(TestContext.Current.CancellationToken);
      Assert.False(healthy);
    }

    #endregion

    #region Before Initialization -- Direct Driver Access Throws

    [Fact]
    public void DirectAccess_ContainerDriver_BeforeInit_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() => _ = pack.ContainerDriver);
    }

    [Fact]
    public void DirectAccess_ImageDriver_BeforeInit_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() => _ = pack.ImageDriver);
    }

    [Fact]
    public void DirectAccess_NetworkDriver_BeforeInit_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() => _ = pack.NetworkDriver);
    }

    [Fact]
    public void DirectAccess_VolumeDriver_BeforeInit_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() => _ = pack.VolumeDriver);
    }

    [Fact]
    public void DirectAccess_SystemDriver_BeforeInit_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() => _ = pack.SystemDriver);
    }

    [Fact]
    public void DirectAccess_ComposeDriver_BeforeInit_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() => _ = pack.ComposeDriver);
    }

    [Fact]
    public void DirectAccess_AuthDriver_BeforeInit_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() => _ = pack.AuthDriver);
    }

    [Fact]
    public void DirectAccess_StreamDriver_BeforeInit_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() => _ = pack.StreamDriver);
    }

    [Fact]
    public void DirectAccess_StackDriver_BeforeInit_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() => _ = pack.StackDriver);
    }

    [Fact]
    public void DirectAccess_ServiceDriver_BeforeInit_ThrowsInvalidOperation()
    {
      var pack = new DockerCliDriverPack();
      Assert.Throws<InvalidOperationException>(() => _ = pack.ServiceDriver);
    }

    #endregion

    #region After Initialization (via reflection)

    /// <summary>
    /// Forces the pack into an initialized state by setting the private
    /// _initialized field and _drivers dictionary via reflection, bypassing
    /// InitializeAsync (which requires a real binary resolver that probes
    /// the filesystem for docker).
    /// </summary>
    private static DockerCliDriverPack CreateInitializedPack(
        out Dictionary<Type, object> driversDict)
    {
      var pack = new DockerCliDriverPack();

      // Set _initialized = true
      var initField = typeof(DockerCliDriverPack)
          .GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance);
      Assert.NotNull(initField);
      initField.SetValue(pack, true);

      // Get the _drivers dictionary
      var driversField = typeof(DockerCliDriverPack)
          .GetField("_drivers", BindingFlags.NonPublic | BindingFlags.Instance);
      Assert.NotNull(driversField);
      driversDict = (Dictionary<Type, object>)driversField.GetValue(pack);

      return pack;
    }

    /// <summary>
    /// Populates the _drivers dictionary with Moq mocks for all 10 registered interfaces.
    /// </summary>
    private static void PopulateWithMocks(Dictionary<Type, object> drivers)
    {
      drivers[typeof(IContainerDriver)] = new Mock<IContainerDriver>().Object;
      drivers[typeof(IImageDriver)] = new Mock<IImageDriver>().Object;
      drivers[typeof(INetworkDriver)] = new Mock<INetworkDriver>().Object;
      drivers[typeof(IVolumeDriver)] = new Mock<IVolumeDriver>().Object;
      drivers[typeof(ISystemDriver)] = new Mock<ISystemDriver>().Object;
      drivers[typeof(IComposeDriver)] = new Mock<IComposeDriver>().Object;
      drivers[typeof(IAuthDriver)] = new Mock<IAuthDriver>().Object;
      drivers[typeof(IStreamDriver)] = new Mock<IStreamDriver>().Object;
      drivers[typeof(IStackDriver)] = new Mock<IStackDriver>().Object;
      drivers[typeof(IServiceDriver)] = new Mock<IServiceDriver>().Object;
    }

    [Fact]
    public void GetSupportedInterfaces_AfterInit_ReturnsExpectedInterfaces()
    {
      var pack = CreateInitializedPack(out var drivers);
      PopulateWithMocks(drivers);

      var supported = pack.GetSupportedInterfaces();

      Assert.Equal(10, supported.Count);
      Assert.Contains(typeof(IContainerDriver), supported);
      Assert.Contains(typeof(IImageDriver), supported);
      Assert.Contains(typeof(INetworkDriver), supported);
      Assert.Contains(typeof(IVolumeDriver), supported);
      Assert.Contains(typeof(ISystemDriver), supported);
      Assert.Contains(typeof(IComposeDriver), supported);
      Assert.Contains(typeof(IAuthDriver), supported);
      Assert.Contains(typeof(IStreamDriver), supported);
      Assert.Contains(typeof(IStackDriver), supported);
      Assert.Contains(typeof(IServiceDriver), supported);
    }

    [Fact]
    public void TryResolve_AfterInit_KnownInterface_Succeeds()
    {
      var pack = CreateInitializedPack(out var drivers);
      PopulateWithMocks(drivers);

      var resolved = pack.TryResolve(typeof(IContainerDriver), out var impl);

      Assert.True(resolved);
      Assert.NotNull(impl);
      Assert.IsAssignableFrom<IContainerDriver>(impl);
    }

    [Fact]
    public void TryResolve_AfterInit_UnknownInterface_ReturnsFalse()
    {
      var pack = CreateInitializedPack(out var drivers);
      PopulateWithMocks(drivers);

      var resolved = pack.TryResolve(typeof(IDisposable), out var impl);

      Assert.False(resolved);
      Assert.Null(impl);
    }

    [Theory]
    [InlineData(typeof(IContainerDriver))]
    [InlineData(typeof(IImageDriver))]
    [InlineData(typeof(INetworkDriver))]
    [InlineData(typeof(IVolumeDriver))]
    [InlineData(typeof(ISystemDriver))]
    [InlineData(typeof(IComposeDriver))]
    [InlineData(typeof(IAuthDriver))]
    [InlineData(typeof(IStreamDriver))]
    [InlineData(typeof(IStackDriver))]
    [InlineData(typeof(IServiceDriver))]
    public void TryResolve_AfterInit_EachRegisteredInterface_Succeeds(Type interfaceType)
    {
      var pack = CreateInitializedPack(out var drivers);
      PopulateWithMocks(drivers);

      var resolved = pack.TryResolve(interfaceType, out var impl);

      Assert.True(resolved);
      Assert.NotNull(impl);
    }

    [Fact]
    public void SysCtlGeneric_AfterInit_KnownInterface_ReturnsDriver()
    {
      var pack = CreateInitializedPack(out var drivers);
      PopulateWithMocks(drivers);

      var driver = pack.SysCtl<IContainerDriver>("docker");
      Assert.NotNull(driver);
    }

    [Fact]
    public void SysCtlGeneric_AfterInit_UnknownInterface_ThrowsInterfaceNotSupported()
    {
      var pack = CreateInitializedPack(out var drivers);
      PopulateWithMocks(drivers);

      Assert.Throws<InterfaceNotSupportedException>(() =>
          pack.SysCtl<IDisposable>("docker"));
    }

    [Fact]
    public void TrySysCtl_AfterInit_KnownInterface_ReturnsTrue()
    {
      var pack = CreateInitializedPack(out var drivers);
      PopulateWithMocks(drivers);

      var found = pack.TrySysCtl<IImageDriver>("docker", out var driver);
      Assert.True(found);
      Assert.NotNull(driver);
    }

    [Fact]
    public void TrySysCtl_AfterInit_UnknownInterface_ReturnsFalse()
    {
      var pack = CreateInitializedPack(out var drivers);
      PopulateWithMocks(drivers);

      var found = pack.TrySysCtl<IDisposable>("docker", out var instance);
      Assert.False(found);
      Assert.Null(instance);
    }

    [Theory]
    [InlineData(DriverComponent.Container)]
    [InlineData(DriverComponent.Image)]
    [InlineData(DriverComponent.Network)]
    [InlineData(DriverComponent.Volume)]
    [InlineData(DriverComponent.System)]
    [InlineData(DriverComponent.Compose)]
    public void SysCtlByComponent_AfterInit_KnownComponent_ReturnsNonNull(
        DriverComponent component)
    {
      // Use reflection to set the private driver fields so the component switch works
      var pack = CreateInitializedPackWithDriverFields();

      var result = pack.SysCtl("docker", component);
      Assert.NotNull(result);
    }

    [Fact]
    public void SysCtlByComponent_AfterInit_UnknownComponent_ThrowsArgumentException()
    {
      var pack = CreateInitializedPackWithDriverFields();

      Assert.Throws<ArgumentException>(() =>
          pack.SysCtl("docker", DriverComponent.Pod));
    }

    [Fact]
    public void SysCtlByType_AfterInit_KnownType_ReturnsDriver()
    {
      var pack = CreateInitializedPack(out var drivers);
      PopulateWithMocks(drivers);

#pragma warning disable CA2263
      var result = pack.SysCtl("docker", typeof(IContainerDriver));
#pragma warning restore CA2263
      Assert.NotNull(result);
    }

    [Fact]
    public void SysCtlByType_AfterInit_UnknownType_ThrowsInterfaceNotSupported()
    {
      var pack = CreateInitializedPack(out var drivers);
      PopulateWithMocks(drivers);

#pragma warning disable CA2263
      Assert.Throws<InterfaceNotSupportedException>(() =>
          pack.SysCtl("docker", typeof(IDisposable)));
#pragma warning restore CA2263
    }

    /// <summary>
    /// Creates an initialized pack with the private driver fields populated
    /// using real concrete driver instances (needed for the SysCtl(string,
    /// DriverComponent) overload which reads the concrete _containerDriver,
    /// _imageDriver, etc. fields directly).
    /// </summary>
    private static DockerCliDriverPack CreateInitializedPackWithDriverFields()
    {
      var pack = CreateInitializedPack(out var drivers);
      PopulateWithMocks(drivers);

      var mockResolver = new Mock<IBinaryResolver>().Object;
      var bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;

      // The private fields are typed as concrete classes, not interfaces,
      // so we must use actual instances rather than Moq proxies.
      SetField(pack, "_containerDriver", new DockerCliContainerDriver(mockResolver), bindingFlags);
      SetField(pack, "_imageDriver", new DockerCliImageDriver(mockResolver), bindingFlags);
      SetField(pack, "_networkDriver", new DockerCliNetworkDriver(mockResolver), bindingFlags);
      SetField(pack, "_volumeDriver", new DockerCliVolumeDriver(mockResolver), bindingFlags);
      SetField(pack, "_systemDriver", new DockerCliSystemDriver(mockResolver), bindingFlags);
      SetField(pack, "_composeDriver", new DockerCliComposeDriver(mockResolver), bindingFlags);

      return pack;
    }

    private static void SetField(object obj, string fieldName, object value,
        BindingFlags flags)
    {
      var field = obj.GetType().GetField(fieldName, flags);
      Assert.NotNull(field);
      field.SetValue(obj, value);
    }

    #endregion

    #region InitializeAsync

    [Fact]
    public async Task InitializeAsync_NullContext_ThrowsArgumentNullException()
    {
      var pack = new DockerCliDriverPack();
      await Assert.ThrowsAsync<ArgumentNullException>(() =>
          pack.InitializeAsync(null, TestContext.Current.CancellationToken));
    }

    #endregion

    #region BinaryResolver Property

    [Fact]
    public void BinaryResolver_BeforeInit_ReturnsNull()
    {
      var pack = new DockerCliDriverPack();
      Assert.Null(pack.BinaryResolver);
    }

    #endregion
  }
}
