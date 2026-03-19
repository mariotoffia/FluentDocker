using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Volumes;
using Moq;
using DriverCapabilities = FluentDocker.Model.Drivers.DriverCapabilities;
using DriverComponent = FluentDocker.Model.Drivers.DriverComponent;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;
using DriverResponse = FluentDocker.Model.Drivers.CommandResponse<FluentDocker.Model.Drivers.Unit>;
using DriverType = FluentDocker.Model.Drivers.DriverType;
using RuntimeType = FluentDocker.Model.Drivers.RuntimeType;
using Unit = FluentDocker.Model.Drivers.Unit;

#pragma warning disable CS0618 // DriverComponent obsolete — intentional usage

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// Mock driver pack for unit testing FluentDocker components.
  /// Provides mock implementations of all driver interfaces.
  /// </summary>
  public partial class MockDriverPack : IDriverPack
  {
    private readonly Dictionary<Type, object> _drivers = new Dictionary<Type, object>();
    private bool _initialized;
    private DriverCapabilities _capabilities;
    private bool _isHealthy = true;

    /// <summary>
    /// Mock container driver.
    /// </summary>
    public Mock<IContainerDriver> ContainerDriver { get; } = new Mock<IContainerDriver>();

    /// <summary>
    /// Mock image driver.
    /// </summary>
    public Mock<IImageDriver> ImageDriver { get; } = new Mock<IImageDriver>();

    /// <summary>
    /// Mock network driver.
    /// </summary>
    public Mock<INetworkDriver> NetworkDriver { get; } = new Mock<INetworkDriver>();

    /// <summary>
    /// Mock volume driver.
    /// </summary>
    public Mock<IVolumeDriver> VolumeDriver { get; } = new Mock<IVolumeDriver>();

    /// <summary>
    /// Mock compose driver.
    /// </summary>
    public Mock<IComposeDriver> ComposeDriver { get; } = new Mock<IComposeDriver>();

    /// <summary>
    /// Mock system driver.
    /// </summary>
    public Mock<ISystemDriver> SystemDriver { get; } = new Mock<ISystemDriver>();

    /// <inheritdoc />
    public DriverType Type => DriverType.DockerCli;

    /// <inheritdoc />
    public RuntimeType Runtime => RuntimeType.Docker;

    /// <summary>
    /// Creates a new mock driver pack with default setups.
    /// </summary>
    public MockDriverPack()
    {
      _capabilities = DriverCapabilities.Default();
      SetupDefaultBehaviors();
    }

    /// <summary>
    /// Sets the capabilities to return from GetCapabilitiesAsync.
    /// </summary>
    /// <param name="capabilities">The capabilities to return.</param>
    public void SetCapabilities(DriverCapabilities capabilities)
    {
      _capabilities = capabilities;
    }

    /// <summary>
    /// Sets whether the driver pack reports as healthy.
    /// </summary>
    /// <param name="isHealthy">True if healthy, false otherwise.</param>
    public void SetHealthy(bool isHealthy)
    {
      _isHealthy = isHealthy;
    }

    /// <inheritdoc />
    public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
      _drivers[typeof(IContainerDriver)] = ContainerDriver.Object;
      _drivers[typeof(IImageDriver)] = ImageDriver.Object;
      _drivers[typeof(INetworkDriver)] = NetworkDriver.Object;
      _drivers[typeof(IVolumeDriver)] = VolumeDriver.Object;
      _drivers[typeof(IComposeDriver)] = ComposeDriver.Object;
      _drivers[typeof(ISystemDriver)] = SystemDriver.Object;

      _initialized = true;
      return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
      return Task.FromResult(_capabilities);
    }

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
      return Task.FromResult(_isHealthy);
    }

    /// <inheritdoc />
    public T SysCtl<T>(string driverId) where T : class
    {
      if (!_initialized)
        throw new InvalidOperationException("MockDriverPack not initialized. Call InitializeAsync first.");

      var requestedType = typeof(T);

      if (_drivers.TryGetValue(requestedType, out var driver))
      {
        return (T)driver;
      }

      throw new InterfaceNotSupportedException(driverId, requestedType.Name);
    }

    /// <inheritdoc />
    public object SysCtl(string driverId, DriverComponent component)
    {
      if (!_initialized)
        throw new InvalidOperationException("MockDriverPack not initialized. Call InitializeAsync first.");

      return component switch
      {
        DriverComponent.Container => ContainerDriver.Object,
        DriverComponent.Image => ImageDriver.Object,
        DriverComponent.Network => NetworkDriver.Object,
        DriverComponent.Volume => VolumeDriver.Object,
        DriverComponent.System => SystemDriver.Object,
        DriverComponent.Compose => ComposeDriver.Object,
        _ => throw new InterfaceNotSupportedException(driverId, component.ToString())
      };
    }

    #region IDriverInterfaceResolver

    /// <inheritdoc />
    public bool TryResolve(Type interfaceType, out object implementation)
    {
      if (!_initialized)
        throw new InvalidOperationException("MockDriverPack not initialized.");
      return _drivers.TryGetValue(interfaceType, out implementation);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Type> GetSupportedInterfaces()
    {
      if (!_initialized)
        throw new InvalidOperationException("MockDriverPack not initialized.");
      return _drivers.Keys.ToList().AsReadOnly();
    }

    #endregion

    #region ISysCtl Type-Based Resolution

    /// <inheritdoc />
    public object SysCtl(string driverId, Type interfaceType)
    {
      if (!_initialized)
        throw new InvalidOperationException("MockDriverPack not initialized. Call InitializeAsync first.");
      if (_drivers.TryGetValue(interfaceType, out var driver))
        return driver;
      throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
    }

    /// <inheritdoc />
    public bool TrySysCtl<T>(string driverId, out T instance) where T : class
    {
      if (!_initialized)
        throw new InvalidOperationException("MockDriverPack not initialized. Call InitializeAsync first.");
      if (_drivers.TryGetValue(typeof(T), out var driver))
      {
        instance = (T)driver;
        return true;
      }
      instance = null;
      return false;
    }

    #endregion

    #region Custom Driver Registration

    /// <summary>
    /// Registers a custom driver interface for testing.
    /// Allows tests to inject arbitrary interfaces (e.g., IPodmanPodDriver).
    /// </summary>
    public void RegisterCustomDriver<T>(T implementation) where T : class
    {
      _drivers[typeof(T)] = implementation;
    }

    #endregion

    /// <summary>
    /// Sets up default behaviors for all mock drivers.
    /// </summary>
    private void SetupDefaultBehaviors()
    {
      // Default system driver ping
      SystemDriver
          .Setup(d => d.PingAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
    }

    #region Helper Setup Methods

    /// <summary>
    /// Sets up ContainerDriver.CreateAsync to return success with the specified container ID.
    /// </summary>
    public MockDriverPack SetupContainerCreate(string containerId = "test-container-123")
    {
      ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<ContainerCreateResult>.Ok(
              new ContainerCreateResult { Id = containerId }));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.RunAsync to return success with the specified container ID.
    /// </summary>
    public MockDriverPack SetupContainerRun(string containerId = "test-container-123")
    {
      ContainerDriver
          .Setup(d => d.RunAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<ContainerRunResult>.Ok(
              new ContainerRunResult { Id = containerId }));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.StartAsync to return success.
    /// </summary>
    public MockDriverPack SetupContainerStart()
    {
      ContainerDriver
          .Setup(d => d.StartAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.StopAsync to return success.
    /// </summary>
    public MockDriverPack SetupContainerStop()
    {
      ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.RemoveAsync to return success.
    /// </summary>
    public MockDriverPack SetupContainerRemove()
    {
      ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.InspectAsync to return a running container.
    /// </summary>
    public MockDriverPack SetupContainerInspect(string containerId = "test-container-123", bool running = true)
    {
      ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Container>.Ok(new Container
          {
            Id = containerId,
            Name = "test-container",
            State = new ContainerState
            {
              Running = running,
              Status = running ? "running" : "exited"
            }
          }));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.ListAsync to return the specified containers.
    /// </summary>
    public MockDriverPack SetupContainerList(params Container[] containers)
    {
      ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Container>>.Ok(
              new List<Container>(containers)));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.PauseAsync to return success.
    /// </summary>
    public MockDriverPack SetupContainerPause()
    {
      ContainerDriver
          .Setup(d => d.PauseAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up NetworkDriver.CreateAsync to return success.
    /// </summary>
    public MockDriverPack SetupNetworkCreate(string networkId = "test-network-123")
    {
      NetworkDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<NetworkCreateResult>.Ok(
              new NetworkCreateResult { Id = networkId }));
      return this;
    }

    /// <summary>
    /// Sets up NetworkDriver.ListAsync to return specified networks.
    /// </summary>
    public MockDriverPack SetupNetworkList(params Network[] networks)
    {
      NetworkDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Network>>.Ok(
              new List<Network>(networks)));
      return this;
    }

    /// <summary>
    /// Sets up NetworkDriver.RemoveAsync to return success.
    /// </summary>
    public MockDriverPack SetupNetworkRemove()
    {
      NetworkDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up NetworkDriver.InspectAsync to return network details.
    /// </summary>
    public MockDriverPack SetupNetworkInspect(string networkId = "test-network-123")
    {
      NetworkDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Network>.Ok(new Network
          {
            Id = networkId,
            Name = "test-network",
            Driver = "bridge"
          }));
      return this;
    }

    /// <summary>
    /// Sets up VolumeDriver.CreateAsync to return success.
    /// </summary>
    public MockDriverPack SetupVolumeCreate(string volumeName = "test-volume")
    {
      VolumeDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<VolumeCreateResult>.Ok(
              new VolumeCreateResult { Name = volumeName, Driver = "local" }));
      return this;
    }

    /// <summary>
    /// Sets up VolumeDriver.RemoveAsync to return success.
    /// </summary>
    public MockDriverPack SetupVolumeRemove()
    {
      VolumeDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up VolumeDriver.InspectAsync to return volume details.
    /// </summary>
    public MockDriverPack SetupVolumeInspect(string volumeName = "test-volume")
    {
      VolumeDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Volume>.Ok(new Volume
          {
            Name = volumeName,
            Driver = "local"
          }));
      return this;
    }

    /// <summary>
    /// Sets up ImageDriver.PullAsync to return success.
    /// </summary>
    public MockDriverPack SetupImagePull()
    {
      ImageDriver
          .Setup(d => d.PullAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<IProgress<ImagePullProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ImageDriver.InspectAsync to return image details.
    /// </summary>
    public MockDriverPack SetupImageInspect(string imageId = "sha256:abc123")
    {
      ImageDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Image>.Ok(new Image
          {
            Id = imageId
          }));
      return this;
    }

    /// <summary>
    /// Sets up ImageDriver.RemoveAsync to return success.
    /// </summary>
    public MockDriverPack SetupImageRemove()
    {
      ImageDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<ImageRemoveResult>.Ok(
              new ImageRemoveResult()));
      return this;
    }

    #endregion
  }
}
