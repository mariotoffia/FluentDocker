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
  public class PodmanCliDriverPack : IDriverPack
  {
    private readonly Dictionary<Type, object> _drivers = [];
    private DriverContext _context;
    private IPodmanBinaryResolver _binaryResolver;
    private ILogger<PodmanCliDriverPack> _logger = NullLogger<PodmanCliDriverPack>.Instance;
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
      cancellationToken.ThrowIfCancellationRequested();
      ArgumentNullException.ThrowIfNull(context);
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

      throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
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

    /// <summary>
    /// Per-machine-name async locks so concurrent kernel builds cannot race to start the
    /// same Podman machine (start/init are not safe to run twice in parallel). Keyed by the
    /// configured machine name (or a sentinel for the default machine).
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> MachineLocks = new();

    /// <summary>
    /// Whether Podman machine management applies on the current platform. Podman machine only
    /// exists on macOS/Windows; on native Linux Podman runs without a VM, so there is nothing
    /// to start. Public static so the platform gate can be unit-tested through the public
    /// surface (the pack's auto-start path itself drives the real <c>podman machine</c> CLI).
    /// </summary>
    public static bool MachineManagementApplies() => FdOs.IsOsx() || FdOs.IsWindows();

    /// <summary>
    /// Computes the per-machine serialization key used to ensure concurrent kernel builds do
    /// not race to start/init the same Podman machine. An unset name normalizes to a shared
    /// <c>"default"</c> sentinel, so a null-name build and an explicit <c>MachineName = "default"</c>
    /// build serialize on the same gate instead of two different ones (conservative
    /// over-serialization is harmless here). Public static so the keying is unit-testable
    /// without internals access.
    /// </summary>
    public static string MachineLockKey(string machineName)
        => string.IsNullOrEmpty(machineName) ? "default" : machineName;

    private async Task AutoStartMachineAsync(
        DriverContext context, CancellationToken cancellationToken)
    {
      // Podman machine only applies on macOS/Windows; on native Linux Podman runs without a
      // VM, so there is nothing to start. Auto-start is only reached when the caller explicitly
      // configured it, so fail loudly instead of silently swallowing the unsupported request.
      if (!MachineManagementApplies())
      {
        throw new DriverException(
            "Podman machine auto-start is only supported on macOS/Windows; native Linux runs Podman without a machine. Remove WithAutoStartMachine on Linux.",
            ErrorCodes.Driver.CapabilityNotSupported);
      }

      // Serialize per machine name so parallel kernel builds do not both try to start/init it.
      var key = MachineLockKey(context.AutoStartMachine.MachineName);
      var gate = MachineLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

      await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        await AutoStartMachineCoreAsync(context, cancellationToken).ConfigureAwait(false);
      }
      finally
      {
        gate.Release();
      }
    }

    private async Task AutoStartMachineCoreAsync(
        DriverContext context, CancellationToken cancellationToken)
    {
      var config = context.AutoStartMachine;
      var listResult = await _machineDriver.ListAsync(context, cancellationToken).ConfigureAwait(false);

      // Retry once on transient failure (e.g. concurrent Podman CLI access)
      if (!listResult.Success || listResult.Data.Count == 0)
      {
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        listResult = await _machineDriver.ListAsync(context, cancellationToken).ConfigureAwait(false);
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

      if (target != null && target.Starting)
      {
        await WaitForMachineReadyAsync(context, cancellationToken).ConfigureAwait(false);
        return;
      }

      if (target != null)
      {
        // Machine exists but is not running — start it, then wait until it actually answers so
        // we do not hand the caller a machine that fails the very next command.
        var startResult = await _machineDriver.StartAsync(
            context, target.Name, cancellationToken).ConfigureAwait(false);

        if (!startResult.Success)
        {
          // Another OS process may have started the machine concurrently ("already
          // running" while the VM is still booting) — poll readiness before declaring
          // failure instead of trusting a single ping.
          try
          {
            await WaitForMachineReadyAsync(context, cancellationToken).ConfigureAwait(false);
            return;
          }
          catch (PodmanMachineNotRunningException)
          {
            throw new PodmanMachineNotRunningException(
                $"Failed to start Podman machine '{target.Name}': {startResult.Error}");
          }
        }

        await WaitForMachineReadyAsync(context, cancellationToken).ConfigureAwait(false);
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

      // Init a new machine. Leave the name unset when unspecified so podman targets its real
      // built-in default instead of a literal machine called "default".
      var displayName = string.IsNullOrEmpty(config.MachineName)
          ? "default machine" : $"machine '{config.MachineName}'";
      var initConfig = BuildAutoStartInitConfig(config);

      var initResult = await _machineDriver.InitAsync(
          context, initConfig, cancellationToken).ConfigureAwait(false);

      if (!initResult.Success)
        throw new PodmanMachineNotRunningException(
            $"Failed to initialize Podman {displayName}: {initResult.Error}");

      await WaitForMachineReadyAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps an <see cref="AutoStartMachineConfig"/> to the <see cref="MachineInitConfig"/> used to
    /// auto-create a machine. When <see cref="AutoStartMachineConfig.MachineName"/> is unspecified
    /// the name is left NULL so <c>podman machine init</c> targets its real built-in default rather
    /// than a literal machine called "default". Public static so the name-omission is unit-testable
    /// through the public surface without internals access.
    /// </summary>
    public static MachineInitConfig BuildAutoStartInitConfig(AutoStartMachineConfig config)
    {
      ArgumentNullException.ThrowIfNull(config);

      return new MachineInitConfig
      {
        Name = string.IsNullOrEmpty(config.MachineName) ? null : config.MachineName,
        Cpus = config.InitCpus,
        MemoryMiB = config.InitMemoryMiB,
        DiskSizeGiB = config.InitDiskSizeGiB,
        Rootful = config.InitRootful,
        Now = true // Start immediately after init
      };
    }

    // ponytail: a fixed 1s poll / 60s ceiling is enough for a local podman machine to answer
    // `info` after start/init. Upgrade path: surface these as knobs on AutoStartMachineConfig if a
    // slower host or CI ever needs a longer readiness budget.
    private static readonly TimeSpan MachineReadyPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MachineReadyTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Polls <c>podman info</c> (via the system driver ping already used by
    /// <see cref="IsHealthyAsync"/>) after a start/init until the machine answers or
    /// <see cref="MachineReadyTimeout"/> elapses. A freshly started machine is not immediately
    /// usable — the VM/connection needs a moment — so returning before it is ready would hand the
    /// caller a machine that fails the next command. Honors caller cancellation.
    /// </summary>
    private async Task WaitForMachineReadyAsync(
        DriverContext context, CancellationToken cancellationToken)
    {
      using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      budget.CancelAfter(MachineReadyTimeout);

      try
      {
        while (true)
        {
          var ping = await _systemDriver.PingAsync(context, budget.Token).ConfigureAwait(false);
          if (ping.Success)
            return;

          await Task.Delay(MachineReadyPollInterval, budget.Token).ConfigureAwait(false);
        }
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        // The readiness budget elapsed (not the caller): surface a clear machine-not-ready error.
        throw new PodmanMachineNotRunningException(
            $"Podman machine did not become ready within {MachineReadyTimeout.TotalSeconds:0}s after start/init.");
      }
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
