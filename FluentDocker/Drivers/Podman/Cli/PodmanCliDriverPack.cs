#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Podman.Cli
{
  /// <summary>
  /// Podman CLI driver pack that composes all individual Podman CLI driver implementations.
  /// Implements IDriverPack for unified access.
  /// </summary>
  /// <remarks>
  /// Unlike Docker, Podman does not support Compose, Stack, or Service (Swarm) drivers.
  /// Podman-specific features like Pods are available via IPodmanPodDriver.
  /// </remarks>
  public partial class PodmanCliDriverPack : IDriverPack, IAsyncDisposable
  {
    private readonly Dictionary<Type, object> _drivers = [];
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private DriverContext _context;
    private IPodmanBinaryResolver _binaryResolver;
    private ILogger<PodmanCliDriverPack> _logger = NullLogger<PodmanCliDriverPack>.Instance;
    // Written with Volatile.Write after all driver fields/_drivers entries are assigned and read
    // with Volatile.Read on lock-free paths (mirrors _disposed), so the release/acquire pairing
    // publishes those writes to readers on weakly ordered hardware (ARM64). _drivers itself is a
    // readonly dictionary fully populated before that volatile publication, so its lock-free
    // readers need no further synchronization.
    private bool _initialized;
    private int _disposed;

    /// <summary>
    /// Gets the binary resolver for this driver pack.
    /// </summary>
    public IPodmanBinaryResolver BinaryResolver => _binaryResolver;

    private PodmanCliContainerDriver _containerDriver;
    private PodmanCliImageDriver _imageDriver;
    private PodmanCliNetworkDriver _networkDriver;
    private PodmanCliVolumeDriver _volumeDriver;
    private PodmanCliSystemDriver _systemDriver;
    private PodmanCliAuthDriver _authDriver;
    private PodmanCliStreamDriver _streamDriver;
    private PodmanCliPodDriver _podDriver;
    private PodmanCliKubernetesDriver _kubernetesDriver;
    private PodmanCliMachineDriver _machineDriver;
    private PodmanCliManifestDriver _manifestDriver;

    /// <inheritdoc />
    public DriverType Type => DriverType.PodmanCli;

    /// <inheritdoc />
    public RuntimeType Runtime => RuntimeType.Podman;

    /// <inheritdoc />
    public async Task InitializeAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      await _initializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        if (_initialized)
          throw new InvalidOperationException("PodmanCliDriverPack is already initialized.");
        _context = context;
        _logger = context.LoggerFactory.CreateLogger<PodmanCliDriverPack>();

        var binaryConfig = new PodmanBinaryConfiguration
        {
          Sudo = context.Sudo,
          SudoPassword = context.SudoPassword,
          DefaultShell = context.DefaultShell,
          BinaryName = context.BinaryName,
          SearchPaths = context.SearchPaths
        };
        _binaryResolver = new PodmanBinariesResolver(binaryConfig, context.LoggerFactory);
        cancellationToken.ThrowIfCancellationRequested();

        _containerDriver = new PodmanCliContainerDriver(_binaryResolver);
        _imageDriver = new PodmanCliImageDriver(_binaryResolver);
        _networkDriver = new PodmanCliNetworkDriver(_binaryResolver);
        _volumeDriver = new PodmanCliVolumeDriver(_binaryResolver);
        _systemDriver = new PodmanCliSystemDriver(_binaryResolver);
        _authDriver = new PodmanCliAuthDriver(_binaryResolver);
        _streamDriver = new PodmanCliStreamDriver(_binaryResolver);
        _podDriver = new PodmanCliPodDriver(_binaryResolver);
        _kubernetesDriver = new PodmanCliKubernetesDriver(_binaryResolver);
        _machineDriver = new PodmanCliMachineDriver(_binaryResolver);
        _manifestDriver = new PodmanCliManifestDriver(_binaryResolver);

        _containerDriver.Initialize(context);
        _imageDriver.Initialize(context);
        _networkDriver.Initialize(context);
        _volumeDriver.Initialize(context);
        _systemDriver.Initialize(context);
        _authDriver.Initialize(context);
        _streamDriver.Initialize(context);
        _podDriver.Initialize(context);
        _kubernetesDriver.Initialize(context);
        _machineDriver.Initialize(context);
        _manifestDriver.Initialize(context);

        _drivers[typeof(IContainerDriver)] = _containerDriver;
        _drivers[typeof(IImageDriver)] = _imageDriver;
        _drivers[typeof(INetworkDriver)] = _networkDriver;
        _drivers[typeof(IVolumeDriver)] = _volumeDriver;
        _drivers[typeof(ISystemDriver)] = _systemDriver;
        _drivers[typeof(IAuthDriver)] = _authDriver;
        _drivers[typeof(IStreamDriver)] = _streamDriver;
        _drivers[typeof(IPodmanPodDriver)] = _podDriver;
        _drivers[typeof(IPodmanKubernetesDriver)] = _kubernetesDriver;
        _drivers[typeof(IPodmanMachineDriver)] = _machineDriver;
        _drivers[typeof(IPodmanManifestDriver)] = _manifestDriver;

        if (context.AutoStartMachine != null)
          await AutoStartMachineAsync(context, cancellationToken).ConfigureAwait(false);

        ThrowIfDisposed();
        Volatile.Write(ref _initialized, true);
      }
      finally
      {
        _initializeLock.Release();
      }
    }

    /// <inheritdoc />
    public Task<DriverCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
      if (Volatile.Read(ref _disposed) != 0)
        return Task.FromException<DriverCapabilities>(
            new ObjectDisposedException(nameof(PodmanCliDriverPack)));
      return Task.FromResult(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsImages = true,
        SupportsNetworks = true,
        SupportsVolumes = true,
        SupportsCompose = false,
        SupportsSystem = true,
        SupportsPods = true,
        SupportsKubernetes = true,
        SupportsMachines = true,
        SupportsManifests = true
      });
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      if (!Volatile.Read(ref _initialized) || _systemDriver == null)
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
        _logger.LogError(ex, "Podman CLI ping failed");
        return false;
      }
    }

    /// <inheritdoc />
    public T SysCtl<T>(string driverId) where T : class
    {
      ThrowIfNotInitialized();

      if (_drivers.TryGetValue(typeof(T), out var driver))
        return (T)driver;

      throw new InterfaceNotSupportedException(driverId, TypeNameFormatter.Format(typeof(T)));
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
      throw new InterfaceNotSupportedException(driverId, TypeNameFormatter.Format(interfaceType));
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

    /// <summary>Gets the container driver.</summary>
    public IContainerDriver ContainerDriver
    {
      get { ThrowIfNotInitialized(); return _containerDriver; }
    }

    /// <summary>Gets the image driver.</summary>
    public IImageDriver ImageDriver
    {
      get { ThrowIfNotInitialized(); return _imageDriver; }
    }

    /// <summary>Gets the network driver.</summary>
    public INetworkDriver NetworkDriver
    {
      get { ThrowIfNotInitialized(); return _networkDriver; }
    }

    /// <summary>Gets the volume driver.</summary>
    public IVolumeDriver VolumeDriver
    {
      get { ThrowIfNotInitialized(); return _volumeDriver; }
    }

    /// <summary>Gets the system driver.</summary>
    public ISystemDriver SystemDriver
    {
      get { ThrowIfNotInitialized(); return _systemDriver; }
    }

    /// <summary>Gets the auth driver.</summary>
    public IAuthDriver AuthDriver
    {
      get { ThrowIfNotInitialized(); return _authDriver; }
    }

    /// <summary>Gets the stream driver.</summary>
    public IStreamDriver StreamDriver
    {
      get { ThrowIfNotInitialized(); return _streamDriver; }
    }

    /// <summary>Gets the pod driver (Podman-specific).</summary>
    public IPodmanPodDriver PodDriver
    {
      get { ThrowIfNotInitialized(); return _podDriver; }
    }

    /// <summary>Gets the Kubernetes driver (Podman-specific).</summary>
    public IPodmanKubernetesDriver KubernetesDriver
    {
      get { ThrowIfNotInitialized(); return _kubernetesDriver; }
    }

    /// <summary>Gets the machine driver (Podman-specific).</summary>
    public IPodmanMachineDriver MachineDriver
    {
      get { ThrowIfNotInitialized(); return _machineDriver; }
    }

    /// <summary>Gets the manifest driver (Podman-specific).</summary>
    public IPodmanManifestDriver ManifestDriver
    {
      get { ThrowIfNotInitialized(); return _manifestDriver; }
    }

    #endregion

  }
}
