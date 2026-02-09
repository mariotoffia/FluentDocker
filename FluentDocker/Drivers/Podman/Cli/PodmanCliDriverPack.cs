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

namespace FluentDocker.Drivers.Podman.Cli
{
  /// <summary>
  /// Podman CLI driver pack that composes all individual Podman CLI driver implementations.
  /// Implements IDriverPack and IDriverInterfaceResolver for unified access.
  /// </summary>
  /// <remarks>
  /// Unlike Docker, Podman does not support Compose, Stack, or Service (Swarm) drivers.
  /// Podman-specific features like Pods are available via IPodmanPodDriver.
  /// </remarks>
  public class PodmanCliDriverPack : IDriverPack, IDriverInterfaceResolver
  {
    private readonly Dictionary<Type, object> _drivers = new Dictionary<Type, object>();
    private DriverContext _context;
    private IPodmanBinaryResolver _binaryResolver;
    private bool _initialized;

    /// <summary>
    /// Gets the binary resolver for this driver pack.
    /// </summary>
    public IPodmanBinaryResolver BinaryResolver => _binaryResolver;

    // Individual driver components
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
      _context = context ?? throw new ArgumentNullException(nameof(context));

      var binaryConfig = new PodmanBinaryConfiguration
      {
        Sudo = context.Sudo,
        SudoPassword = context.SudoPassword,
        DefaultShell = context.DefaultShell
      };
      _binaryResolver = new PodmanBinariesResolver(binaryConfig);

      // Create all driver components with binary resolver
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

      // Initialize all components with context
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

      // Register all drivers by interface type
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

      // Auto-start machine if configured
      if (context.AutoStartMachine != null)
        await AutoStartMachineAsync(context, cancellationToken);

      _initialized = true;
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
      if (!_initialized || _systemDriver == null)
        return false;

      try
      {
        var result = await _systemDriver.PingAsync(_context, cancellationToken);
        return result.Success;
      }
      catch
      {
        return false;
      }
    }

    /// <inheritdoc />
    public T SysCtl<T>(string driverId) where T : class
    {
      ThrowIfNotInitialized();

      if (_drivers.TryGetValue(typeof(T), out var driver))
        return (T)driver;

      throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
    }

    /// <inheritdoc />
    public object SysCtl(string driverId, DriverComponent component)
    {
      ThrowIfNotInitialized();

      return component switch
      {
        DriverComponent.Container => _containerDriver,
        DriverComponent.Image => _imageDriver,
        DriverComponent.Network => _networkDriver,
        DriverComponent.Volume => _volumeDriver,
        DriverComponent.System => _systemDriver,
        DriverComponent.Pod => _podDriver,
        DriverComponent.Kubernetes => _kubernetesDriver,
        DriverComponent.Machine => _machineDriver,
        DriverComponent.Manifest => _manifestDriver,
        _ => throw new ArgumentException(
            $"Component '{component}' is not supported by Podman driver",
            nameof(component))
      };
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

    #region Auto-Start Machine

    private async Task AutoStartMachineAsync(
        DriverContext context, CancellationToken cancellationToken)
    {
      var config = context.AutoStartMachine;
      var listResult = await _machineDriver.ListAsync(context, cancellationToken);

      // Retry once on transient failure (e.g. concurrent Podman CLI access)
      if (!listResult.Success || listResult.Data.Count == 0)
      {
        await Task.Delay(500, cancellationToken);
        listResult = await _machineDriver.ListAsync(context, cancellationToken);
      }

      if (!listResult.Success)
        throw new PodmanMachineNotRunningException(
            $"Failed to list Podman machines: {listResult.Error}");

      // Find the target machine
      MachineInfo target;
      if (!string.IsNullOrEmpty(config.MachineName))
        target = listResult.Data.FirstOrDefault(
            m => string.Equals(m.Name, config.MachineName,
                StringComparison.OrdinalIgnoreCase));
      else
        target = listResult.Data.FirstOrDefault(m => m.Default)
            ?? listResult.Data.FirstOrDefault();

      if (target != null && target.Running)
        return; // Machine is already running

      if (target != null)
      {
        // Machine exists but is not running — start it
        var startResult = await _machineDriver.StartAsync(
            context, target.Name, cancellationToken);

        if (!startResult.Success)
          throw new PodmanMachineNotRunningException(
              $"Failed to start Podman machine '{target.Name}': {startResult.Error}");

        return;
      }

      // Machine does not exist
      if (!config.CreateIfNotExists)
        throw new PodmanMachineNotRunningException(
            $"No Podman machine found" +
            (string.IsNullOrEmpty(config.MachineName)
                ? ". "
                : $" named '{config.MachineName}'. ") +
            "Start one with: podman machine init && podman machine start");

      // Init a new machine
      var machineName = config.MachineName ?? "default";
      var initConfig = new MachineInitConfig
      {
        Name = machineName,
        Cpus = config.InitCpus,
        MemoryMiB = config.InitMemoryMiB,
        DiskSizeGiB = config.InitDiskSizeGiB,
        Rootful = config.InitRootful,
        Now = true // Start immediately after init
      };

      var initResult = await _machineDriver.InitAsync(
          context, initConfig, cancellationToken);

      if (!initResult.Success)
        throw new PodmanMachineNotRunningException(
            $"Failed to initialize Podman machine '{machineName}': {initResult.Error}");
    }

    #endregion

    private void ThrowIfNotInitialized()
    {
      if (!_initialized)
        throw new InvalidOperationException(
            "PodmanCliDriverPack has not been initialized. Call InitializeAsync first.");
    }
  }
}
