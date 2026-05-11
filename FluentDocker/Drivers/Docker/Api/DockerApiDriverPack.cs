using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Docker.Api
{
  /// <summary>
  /// Docker Engine REST API driver pack. Composes all Docker API driver components.
  /// Implements IDriverPack for unified access.
  /// </summary>
  public class DockerApiDriverPack : IDriverPack, IAsyncDisposable
  {
    private readonly Dictionary<Type, object> _drivers = [];
    private DriverContext _context;
    // CA1859: Must stay as interface — tests inject MockDockerApiConnection via reflection.
#pragma warning disable CA1859
    private IDockerApiConnection _connection;
#pragma warning restore CA1859
    private ILogger<DockerApiDriverPack> _logger = NullLogger<DockerApiDriverPack>.Instance;
    private bool _initialized;

    private DockerApiContainerDriver _containerDriver;
    private DockerApiImageDriver _imageDriver;
    private DockerApiNetworkDriver _networkDriver;
    private DockerApiVolumeDriver _volumeDriver;
    private DockerApiSystemDriver _systemDriver;
    private DockerApiAuthDriver _authDriver;
    private DockerApiStreamDriver _streamDriver;
    private DockerApiServiceDriver _serviceDriver;

    /// <inheritdoc />
    public DriverType Type => DriverType.DockerApi;

    /// <inheritdoc />
    public RuntimeType Runtime => RuntimeType.Docker;

    /// <summary>
    /// Gets the API connection for this driver pack.
    /// </summary>
    public IDockerApiConnection Connection => _connection;

    /// <inheritdoc />
    public async Task InitializeAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(context);
      _context = context;
      _logger = context.LoggerFactory.CreateLogger<DockerApiDriverPack>();

      var connectionConfig = new DockerApiConnectionConfig
      {
        Host = context.Host,
        CertificatePath = context.CertificatePath,
        VerifyTls = context.VerifyTls,
        ConnectionTimeout = context.ConnectionTimeout ?? TimeSpan.FromSeconds(30),
        RequestTimeout = context.RequestTimeout ?? TimeSpan.FromMinutes(5),
        ApiVersion = context.ApiVersion,
      };
      _connection = new DockerApiConnection(connectionConfig, context.LoggerFactory);

      _containerDriver = new DockerApiContainerDriver(_connection);
      _imageDriver = new DockerApiImageDriver(_connection);
      _networkDriver = new DockerApiNetworkDriver(_connection);
      _volumeDriver = new DockerApiVolumeDriver(_connection);
      _systemDriver = new DockerApiSystemDriver(_connection);
      _authDriver = new DockerApiAuthDriver(_connection);
      _streamDriver = new DockerApiStreamDriver(_connection);
      _serviceDriver = new DockerApiServiceDriver(_connection);

      _containerDriver.Initialize(context);
      _imageDriver.Initialize(context);
      _networkDriver.Initialize(context);
      _volumeDriver.Initialize(context);
      _systemDriver.Initialize(context);
      _authDriver.Initialize(context);
      _streamDriver.Initialize(context);
      _serviceDriver.Initialize(context);

      _drivers[typeof(IContainerDriver)] = _containerDriver;
      _drivers[typeof(IImageDriver)] = _imageDriver;
      _drivers[typeof(INetworkDriver)] = _networkDriver;
      _drivers[typeof(IVolumeDriver)] = _volumeDriver;
      _drivers[typeof(ISystemDriver)] = _systemDriver;
      _drivers[typeof(IAuthDriver)] = _authDriver;
      _drivers[typeof(IStreamDriver)] = _streamDriver;
      _drivers[typeof(IServiceDriver)] = _serviceDriver;

      _initialized = true;
      await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DriverCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
      return Task.FromResult(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsImages = true,
        SupportsNetworks = true,
        SupportsVolumes = true,
        SupportsCompose = false,    // Compose V2 is CLI-only
        SupportsSystem = true,
        SupportsPods = false,
        SupportsKubernetes = false,
        SupportsMachines = false,
        SupportsManifests = false,
      });
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
      if (!_initialized || _connection == null)
        return false;

      try
      {
        return await _connection.PingAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Docker API ping failed");
        return false;
      }
    }

    #region ISysCtl

    /// <inheritdoc />
    public T SysCtl<T>(string driverId) where T : class
    {
      ThrowIfNotInitialized();
      if (_drivers.TryGetValue(typeof(T), out var driver))
        return (T)driver;
      throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
    }

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

    #region Direct Driver Access

    public IContainerDriver ContainerDriver
    {
      get { ThrowIfNotInitialized(); return _containerDriver; }
    }

    public IImageDriver ImageDriver
    {
      get { ThrowIfNotInitialized(); return _imageDriver; }
    }

    public INetworkDriver NetworkDriver
    {
      get { ThrowIfNotInitialized(); return _networkDriver; }
    }

    public IVolumeDriver VolumeDriver
    {
      get { ThrowIfNotInitialized(); return _volumeDriver; }
    }

    public ISystemDriver SystemDriver
    {
      get { ThrowIfNotInitialized(); return _systemDriver; }
    }

    public IAuthDriver AuthDriver
    {
      get { ThrowIfNotInitialized(); return _authDriver; }
    }

    public IStreamDriver StreamDriver
    {
      get { ThrowIfNotInitialized(); return _streamDriver; }
    }

    public IServiceDriver ServiceDriver
    {
      get { ThrowIfNotInitialized(); return _serviceDriver; }
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
      if (_connection != null)
        await _connection.DisposeAsync().ConfigureAwait(false);

      GC.SuppressFinalize(this);
    }

    private void ThrowIfNotInitialized()
    {
      if (!_initialized)
        throw new InvalidOperationException(
            "DockerApiDriverPack has not been initialized. Call InitializeAsync first.");
    }
  }
}
