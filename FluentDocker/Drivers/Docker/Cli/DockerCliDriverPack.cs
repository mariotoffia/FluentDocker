using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli
{
  /// <summary>
  /// Docker CLI driver pack that composes all individual Docker CLI driver implementations.
  /// Implements IDriverPack for unified access.
  /// </summary>
  public class DockerCliDriverPack : IDriverPack
  {
    private readonly Dictionary<Type, object> _drivers = new Dictionary<Type, object>();
    private DriverContext _context;
    private IBinaryResolver _binaryResolver;
    private bool _initialized;

    /// <summary>
    /// Gets the binary resolver for this driver pack.
    /// </summary>
    public IBinaryResolver BinaryResolver => _binaryResolver;

    /// <summary>
    /// Individual driver components
    /// </summary>
    private DockerCliContainerDriver _containerDriver;
    private DockerCliImageDriver _imageDriver;
    private DockerCliNetworkDriver _networkDriver;
    private DockerCliVolumeDriver _volumeDriver;
    private DockerCliSystemDriver _systemDriver;
    private DockerCliComposeDriver _composeDriver;
    private DockerCliAuthDriver _authDriver;
    private DockerCliStreamDriver _streamDriver;
    private DockerCliStackDriver _stackDriver;
    private DockerCliServiceDriver _serviceDriver;

    /// <inheritdoc />
    public DriverType Type => DriverType.DockerCli;

    /// <inheritdoc />
    public RuntimeType Runtime => RuntimeType.Docker;

    /// <inheritdoc />
    public async Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(context);
      _context = context;

      // Initialize the binary resolver with context configuration
      var binaryConfig = new BinaryConfiguration
      {
        Sudo = context.Sudo,
        SudoPassword = context.SudoPassword,
        DefaultShell = context.DefaultShell
      };
      _binaryResolver = new DockerBinariesResolver(binaryConfig);

      // Create and initialize all driver components with binary resolver
      _containerDriver = new DockerCliContainerDriver(_binaryResolver);
      _imageDriver = new DockerCliImageDriver(_binaryResolver);
      _networkDriver = new DockerCliNetworkDriver(_binaryResolver);
      _volumeDriver = new DockerCliVolumeDriver(_binaryResolver);
      _systemDriver = new DockerCliSystemDriver(_binaryResolver);
      _composeDriver = new DockerCliComposeDriver(_binaryResolver);
      _authDriver = new DockerCliAuthDriver(_binaryResolver);
      _streamDriver = new DockerCliStreamDriver(_binaryResolver);
      _stackDriver = new DockerCliStackDriver(_binaryResolver);
      _serviceDriver = new DockerCliServiceDriver(_binaryResolver);

      // Initialize all components with context
      _containerDriver.Initialize(context);
      _imageDriver.Initialize(context);
      _networkDriver.Initialize(context);
      _volumeDriver.Initialize(context);
      _systemDriver.Initialize(context);
      _composeDriver.Initialize(context);
      _authDriver.Initialize(context);
      _streamDriver.Initialize(context);
      _stackDriver.Initialize(context);
      _serviceDriver.Initialize(context);

      // Register all drivers by interface type
      _drivers[typeof(IContainerDriver)] = _containerDriver;
      _drivers[typeof(IImageDriver)] = _imageDriver;
      _drivers[typeof(INetworkDriver)] = _networkDriver;
      _drivers[typeof(IVolumeDriver)] = _volumeDriver;
      _drivers[typeof(ISystemDriver)] = _systemDriver;
      _drivers[typeof(IComposeDriver)] = _composeDriver;
      _drivers[typeof(IAuthDriver)] = _authDriver;
      _drivers[typeof(IStreamDriver)] = _streamDriver;
      _drivers[typeof(IStackDriver)] = _stackDriver;
      _drivers[typeof(IServiceDriver)] = _serviceDriver;

      _initialized = true;
      await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
      return Task.FromResult(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsImages = true,
        SupportsNetworks = true,
        SupportsVolumes = true,
        SupportsCompose = true,
        SupportsSystem = true,
        SupportsPods = false
      });
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
      if (!_initialized || _systemDriver == null)
        return false;

      try
      {
        var result = await _systemDriver.PingAsync(_context, cancellationToken).ConfigureAwait(false);
        return result.Success;
      }
      catch (Exception ex)
      {
        Logger.Log($"Docker CLI ping failed: {ex.Message}");
        return false;
      }
    }

    /// <inheritdoc />
    public T SysCtl<T>(string driverId) where T : class
    {
      ThrowIfNotInitialized();

      var requestedType = typeof(T);

      if (_drivers.TryGetValue(requestedType, out var driver))
      {
        return (T)driver;
      }

      throw new InterfaceNotSupportedException(driverId, requestedType.Name);
    }

    #region IDriverInterfaceResolver

    /// <inheritdoc />
    public bool TryResolve(Type interfaceType, out object implementation)
    {
      ThrowIfNotInitialized();
      return _drivers.TryGetValue(interfaceType, out implementation);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Type> GetSupportedInterfaces()
    {
      ThrowIfNotInitialized();
      return _drivers.Keys.ToList().AsReadOnly();
    }

    #endregion

    #region ISysCtl Type-Based Resolution

    /// <inheritdoc />
    public object SysCtl(string driverId, Type interfaceType)
    {
      ThrowIfNotInitialized();
      if (_drivers.TryGetValue(interfaceType, out var driver))
        return driver;
      throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
    }

    /// <inheritdoc />
    public bool TrySysCtl<T>(string driverId, out T instance) where T : class
    {
      ThrowIfNotInitialized();
      if (_drivers.TryGetValue(typeof(T), out var driver))
      {
        instance = (T)driver;
        return true;
      }
      instance = null;
      return false;
    }

    #endregion

    #region Direct Driver Access

    /// <summary>
    /// Gets the container driver.
    /// </summary>
    public IContainerDriver ContainerDriver
    {
      get
      {
        ThrowIfNotInitialized();
        return _containerDriver;
      }
    }

    /// <summary>
    /// Gets the image driver.
    /// </summary>
    public IImageDriver ImageDriver
    {
      get
      {
        ThrowIfNotInitialized();
        return _imageDriver;
      }
    }

    /// <summary>
    /// Gets the network driver.
    /// </summary>
    public INetworkDriver NetworkDriver
    {
      get
      {
        ThrowIfNotInitialized();
        return _networkDriver;
      }
    }

    /// <summary>
    /// Gets the volume driver.
    /// </summary>
    public IVolumeDriver VolumeDriver
    {
      get
      {
        ThrowIfNotInitialized();
        return _volumeDriver;
      }
    }

    /// <summary>
    /// Gets the system driver.
    /// </summary>
    public ISystemDriver SystemDriver
    {
      get
      {
        ThrowIfNotInitialized();
        return _systemDriver;
      }
    }

    /// <summary>
    /// Gets the compose driver.
    /// </summary>
    public IComposeDriver ComposeDriver
    {
      get
      {
        ThrowIfNotInitialized();
        return _composeDriver;
      }
    }

    /// <summary>
    /// Gets the auth driver.
    /// </summary>
    public IAuthDriver AuthDriver
    {
      get
      {
        ThrowIfNotInitialized();
        return _authDriver;
      }
    }

    /// <summary>
    /// Gets the stream driver.
    /// </summary>
    public IStreamDriver StreamDriver
    {
      get
      {
        ThrowIfNotInitialized();
        return _streamDriver;
      }
    }

    /// <summary>
    /// Gets the stack driver.
    /// </summary>
    public IStackDriver StackDriver
    {
      get
      {
        ThrowIfNotInitialized();
        return _stackDriver;
      }
    }

    /// <summary>
    /// Gets the service driver.
    /// </summary>
    public IServiceDriver ServiceDriver
    {
      get
      {
        ThrowIfNotInitialized();
        return _serviceDriver;
      }
    }

    #endregion

    #region Private Helpers

    private void ThrowIfNotInitialized()
    {
      if (!_initialized)
      {
        throw new InvalidOperationException("DockerCliDriverPack has not been initialized. Call InitializeAsync first.");
      }
    }

    #endregion
  }
}

