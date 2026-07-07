using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Drivers.Models;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Docker.Cli
{
  /// <summary>
  /// Docker CLI driver pack that composes all individual Docker CLI driver implementations.
  /// Implements IDriverPack for unified access.
  /// </summary>
  public class DockerCliDriverPack : IDriverPack, IAsyncDisposable
  {
    private readonly Dictionary<Type, object> _drivers = [];
    private DriverContext _context;
    private IBinaryResolver _binaryResolver;
    private ILogger<DockerCliDriverPack> _logger = NullLogger<DockerCliDriverPack>.Instance;
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
    private Components.DockerCliModelManagementDriver _modelManagementDriver;
    private Components.DockerCliModelRuntimeDriver _modelRuntimeDriver;
    // Inference is served over the OpenAI-compatible :12434 HTTP data plane — the
    // `docker model` CLI cannot stream tokens or embed, so transport here is an
    // adapter detail, not a user choice. The pack owns the connection's lifetime
    // and builds it lazily on first resolve so pure-container packs pay nothing.
    private ModelRunnerEndpoint _modelEndpoint;
    private ModelApiConnection _modelInferenceConnection;
    private OpenAiModelInferenceDriver _modelInferenceDriver;
    private readonly object _inferenceLock = new();
    private int _disposed;

    /// <inheritdoc />
    public DriverType Type => DriverType.DockerCli;

    /// <inheritdoc />
    public RuntimeType Runtime => RuntimeType.Docker;

    /// <inheritdoc />
    public async Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(context);
      _context = context;
      _logger = context.LoggerFactory.CreateLogger<DockerCliDriverPack>();

      // Initialize the binary resolver with context configuration
      var binaryConfig = new BinaryConfiguration
      {
        Sudo = context.Sudo,
        SudoPassword = context.SudoPassword,
        DefaultShell = context.DefaultShell,
        BinaryName = string.IsNullOrWhiteSpace(context.BinaryName) ? "docker" : context.BinaryName,
        SearchPaths = context.SearchPaths
      };
      _binaryResolver = new DockerBinariesResolver(binaryConfig, context.LoggerFactory);
      cancellationToken.ThrowIfCancellationRequested();

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
      _modelManagementDriver = new Components.DockerCliModelManagementDriver(_binaryResolver);
      _modelRuntimeDriver = new Components.DockerCliModelRuntimeDriver(_binaryResolver);
      // Inference endpoint is pack-owned: bind a configured endpoint (a non-default
      // port/engine) once at registration, else the resolved default
      // (DOCKER_MODEL_RUNNER_URL, else host TCP). The connection is built lazily.
      _modelEndpoint = context.ModelRunnerEndpoint ?? ResolveDefaultModelEndpoint(_logger);

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
      _modelManagementDriver.Initialize(context);
      _modelRuntimeDriver.Initialize(context);

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
      // Docker Model Runner ports: management + runtime via the docker CLI;
      // inference via the OpenAI-compatible HTTP data plane (lazily built on first
      // resolve — see EnsureInferenceDriver — so a pure-container pack pays nothing).
      _drivers[typeof(IModelManagementDriver)] = _modelManagementDriver;
      _drivers[typeof(IModelRuntimeDriver)] = _modelRuntimeDriver;

      _initialized = true;
      await Task.CompletedTask;
    }

    private static ModelRunnerEndpoint ResolveDefaultModelEndpoint(ILogger logger)
    {
      var raw = Environment.GetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable);
      if (ModelRunnerEndpoint.TryFromEnvironment(out var endpoint))
        return endpoint;

      if (!string.IsNullOrWhiteSpace(raw))
      {
        // ponytail: warn-and-fallback so a typo isn't fully swallowed; Try contract stays no-throw.
        logger?.LogWarning(
            "{Variable}='{Value}' is not a valid absolute http(s) URL; falling back to host TCP :12434.",
            ModelRunnerEndpoint.UrlEnvironmentVariable, raw);
      }

      return ModelRunnerEndpoint.HostTcp();
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
        SupportsPods = false,
        SupportsKubernetes = false,
        SupportsMachines = false,
        SupportsManifests = false,
        SupportsStacks = true,
        SupportsServices = true,
        SupportsModels = true
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
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Docker CLI ping failed");
        return false;
      }
    }

    /// <inheritdoc />
    public T SysCtl<T>(string driverId) where T : class
    {
      ThrowIfNotInitialized();

      if (TryGetDriver(typeof(T), out var driver))
        return (T)driver;

      throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
    }

    #region IDriverInterfaceResolver

    /// <inheritdoc />
    public bool TryResolve(Type interfaceType, out object implementation)
    {
      ThrowIfNotInitialized();
      return TryGetDriver(interfaceType, out implementation);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Type> GetSupportedInterfaces()
    {
      ThrowIfNotInitialized();
      // Inference is built lazily and not in the dict, but it is always supported.
      return new List<Type>(_drivers.Keys) { typeof(IModelInferenceDriver) }.AsReadOnly();
    }

    #endregion

    #region ISysCtl Type-Based Resolution

    /// <inheritdoc />
    public object SysCtl(string driverId, Type interfaceType)
    {
      ThrowIfNotInitialized();
      if (TryGetDriver(interfaceType, out var driver))
        return driver;
      throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
    }

    /// <inheritdoc />
    public bool TrySysCtl<T>(string driverId, out T instance) where T : class
    {
      ThrowIfNotInitialized();
      if (TryGetDriver(typeof(T), out var driver))
      {
        instance = (T)driver;
        return true;
      }
      instance = null;
      return false;
    }

    // Resolves a registered driver, building the inference adapter on first request
    // (lazy: pure-container packs never allocate the HttpClient).
    private bool TryGetDriver(Type interfaceType, out object driver)
    {
      ThrowIfDisposed();
      if (interfaceType == typeof(IModelInferenceDriver))
      {
        lock (_inferenceLock)
        {
          ThrowIfDisposed();
          driver = EnsureInferenceDriver();
          return true;
        }
      }
      return _drivers.TryGetValue(interfaceType, out driver);
    }

    private OpenAiModelInferenceDriver EnsureInferenceDriver()
    {
      var existing = Volatile.Read(ref _modelInferenceDriver);
      if (existing != null)
        return existing;

      lock (_inferenceLock)
      {
        ThrowIfDisposed();
        _modelInferenceConnection ??= new ModelApiConnection(_modelEndpoint, loggerFactory: _context?.LoggerFactory);
        _modelInferenceDriver ??= new OpenAiModelInferenceDriver(_modelInferenceConnection, _modelEndpoint);
        return _modelInferenceDriver;
      }
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

    #region IAsyncDisposable

    /// <summary>
    /// Disposes pack-owned resources — currently the inference connection's
    /// <see cref="System.Net.Http.HttpClient"/>. Invoked by the kernel's driver
    /// registry when the kernel is disposed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      ModelApiConnection connection;
      lock (_inferenceLock)
      {
        connection = _modelInferenceConnection;
        _modelInferenceConnection = null;
        _modelInferenceDriver = null;
      }

      if (connection != null)
        await connection.DisposeAsync().ConfigureAwait(false);
      _initialized = false;
      GC.SuppressFinalize(this);
    }

    #endregion

    #region Private Helpers

    private void ThrowIfNotInitialized()
    {
      ThrowIfDisposed();
      if (!_initialized)
      {
        throw new InvalidOperationException("DockerCliDriverPack has not been initialized. Call InitializeAsync first.");
      }
    }

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    #endregion
  }
}
