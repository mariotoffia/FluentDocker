# FluentDocker v3.0.0 - Fluent API and Enhanced Capabilities

## Overview

This document describes the fluent API for driver registration and kernel configuration, along with the enhanced capability system with composable interfaces based on comprehensive Docker and Podman feature analysis.

**Key Enhancements:**
1. Fluent API for driver registration and kernel configuration
2. Composable driver interfaces (break down into smaller, focused interfaces)
3. Granular capability discovery system
4. Feature flags for fine-grained capability detection
5. Migration from static Fd.XXX methods

---

## Fluent API for Driver Registration

### Kernel Configuration Fluent API

```csharp
// Fluent kernel configuration
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker-local")
        .UseDockerCli()
        .AtHost("unix:///var/run/docker.sock")
        .Build()
    .WithDriver("docker-remote")
        .UseDockerApi()
        .AtHost("tcp://remote:2376")
        .WithCertificates("/path/to/certs")
        .WithTimeout(TimeSpan.FromSeconds(30))
        .AsPriority(200)
        .AsDefault()
        .Build()
    .WithDriver("podman")
        .UsePodmanCli()
        .AsRootless()
        .WithPodSupport()
        .Build()
    .WithRetryPolicy()
        .MaxAttempts(3)
        .InitialDelay(TimeSpan.FromSeconds(1))
        .ExponentialBackoff(2.0)
        .Build()
    .WithLogging(logger => logger
        .UseStructuredLogging()
        .MinimumLevel(LogLevel.Information))
    .WithMetrics<MyCustomMetrics>()
    .Build();
```

### Driver Builder Pattern

```csharp
namespace Ductus.FluentDocker.Kernel.Builders
{
    public interface IKernelBuilder
    {
        IDriverBuilder<IKernelBuilder> WithDriver(string driverId);
        IKernelBuilder WithRetryPolicy();
        IKernelBuilder WithLogging(Action<ILoggingBuilder> configure);
        IKernelBuilder WithMetrics<T>() where T : IFluentDockerMetrics, new();
        FluentDockerKernel Build();
    }

    public interface IDriverBuilder<TReturn>
    {
        IDockerCliDriverBuilder<TReturn> UseDockerCli();
        IDockerApiDriverBuilder<TReturn> UseDockerApi();
        IPodmanCliDriverBuilder<TReturn> UsePodmanCli();
        TReturn Build();
    }

    public interface IDockerCliDriverBuilder<TReturn>
    {
        IDockerCliDriverBuilder<TReturn> AtHost(string hostUri);
        IDockerCliDriverBuilder<TReturn> WithCertificates(string certPath);
        IDockerCliDriverBuilder<TReturn> WithTimeout(TimeSpan timeout);
        IDockerCliDriverBuilder<TReturn> WithSudo(SudoMechanism mechanism);
        IDockerCliDriverBuilder<TReturn> AsPriority(int priority);
        IDockerCliDriverBuilder<TReturn> AsDefault();
        IDockerCliDriverBuilder<TReturn> WithComposeV2();
        IDockerCliDriverBuilder<TReturn> WithBuildx();
        TReturn Build();
    }

    public interface IDockerApiDriverBuilder<TReturn>
    {
        IDockerApiDriverBuilder<TReturn> AtHost(string hostUri);
        IDockerApiDriverBuilder<TReturn> WithCertificates(string certPath);
        IDockerApiDriverBuilder<TReturn> WithTimeout(TimeSpan timeout);
        IDockerApiDriverBuilder<TReturn> AsPriority(int priority);
        IDockerApiDriverBuilder<TReturn> AsDefault();
        IDockerApiDriverBuilder<TReturn> WithStreaming();
        IDockerApiDriverBuilder<TReturn> WithBulkOperations();
        TReturn Build();
    }

    public interface IPodmanCliDriverBuilder<TReturn>
    {
        IPodmanCliDriverBuilder<TReturn> AtHost(string hostUri);
        IPodmanCliDriverBuilder<TReturn> AsRootless();
        IPodmanCliDriverBuilder<TReturn> WithPodSupport();
        IPodmanCliDriverBuilder<TReturn> WithKubernetesYaml();
        IPodmanCliDriverBuilder<TReturn> WithSystemdIntegration();
        IPodmanCliDriverBuilder<TReturn> WithReducedCapabilities();
        IPodmanCliDriverBuilder<TReturn> AsPriority(int priority);
        TReturn Build();
    }
}
```

### Implementation

```csharp
public class KernelBuilder : IKernelBuilder
{
    private readonly List<Action<FluentDockerKernel>> _configurations = new();
    private readonly FluentDockerKernelOptions _options = new();

    public static IKernelBuilder Create()
    {
        return new KernelBuilder();
    }

    public IDriverBuilder<IKernelBuilder> WithDriver(string driverId)
    {
        return new DriverBuilder<IKernelBuilder>(this, driverId, _configurations);
    }

    public IKernelBuilder WithRetryPolicy()
    {
        // Configure default retry policy
        return this;
    }

    public IKernelBuilder WithLogging(Action<ILoggingBuilder> configure)
    {
        var loggingBuilder = new LoggingBuilder();
        configure(loggingBuilder);
        _configurations.Add(kernel => loggingBuilder.Apply(kernel));
        return this;
    }

    public IKernelBuilder WithMetrics<T>() where T : IFluentDockerMetrics, new()
    {
        _configurations.Add(kernel => MetricsFactory.SetMetrics(new T()));
        return this;
    }

    public FluentDockerKernel Build()
    {
        var kernel = new FluentDockerKernel(_options);
        foreach (var config in _configurations)
        {
            config(kernel);
        }
        return kernel;
    }
}

public class DockerCliDriverBuilder<TReturn> : IDockerCliDriverBuilder<TReturn>
{
    private readonly TReturn _parent;
    private readonly string _driverId;
    private readonly List<Action<FluentDockerKernel>> _configurations;
    private string _hostUri = "unix:///var/run/docker.sock";
    private string _certPath;
    private TimeSpan? _timeout;
    private SudoMechanism _sudo = SudoMechanism.None;
    private int _priority = 100;
    private bool _isDefault;
    private bool _composeV2;
    private bool _buildx;

    public IDockerCliDriverBuilder<TReturn> AtHost(string hostUri)
    {
        _hostUri = hostUri;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> WithCertificates(string certPath)
    {
        _certPath = certPath;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> AsPriority(int priority)
    {
        _priority = priority;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> AsDefault()
    {
        _isDefault = true;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> WithComposeV2()
    {
        _composeV2 = true;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> WithBuildx()
    {
        _buildx = true;
        return this;
    }

    public TReturn Build()
    {
        _configurations.Add(kernel =>
        {
            var host = new DockerUri(_hostUri);
            var certs = !string.IsNullOrEmpty(_certPath) ? new CertificatePaths(_certPath) : null;

            var driver = new DockerCliDriver(host, certs)
            {
                Timeout = _timeout,
                SudoMechanism = _sudo,
                UseComposeV2 = _composeV2,
                UseBuildx = _buildx
            };

            kernel.RegisterDriver(_driverId, driver, new DriverRegistrationOptions
            {
                Priority = _priority,
                IsDefault = _isDefault
            });
        });

        return _parent;
    }
}
```

### Usage Examples

**Simple local Docker:**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker")
        .UseDockerCli()
        .Build()
    .Build();
```

**Multiple hosts with priorities:**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker-local")
        .UseDockerCli()
        .AsPriority(100)
        .Build()
    .WithDriver("docker-staging")
        .UseDockerApi()
        .AtHost("tcp://staging:2376")
        .WithCertificates("/certs/staging")
        .AsPriority(200)
        .Build()
    .WithDriver("docker-prod")
        .UseDockerApi()
        .AtHost("tcp://prod:2376")
        .WithCertificates("/certs/prod")
        .AsPriority(300)
        .AsDefault()
        .Build()
    .Build();
```

**Docker + Podman:**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker")
        .UseDockerCli()
        .WithComposeV2()
        .WithBuildx()
        .Build()
    .WithDriver("podman")
        .UsePodmanCli()
        .AsRootless()
        .WithPodSupport()
        .WithKubernetesYaml()
        .Build()
    .WithLogging(log => log
        .UseStructuredLogging()
        .MinimumLevel(LogLevel.Information))
    .Build();
```

---

## Composable Driver Interfaces

### Interface Breakdown Strategy

Break down large interfaces (IContainerDriver, INetworkDriver, etc.) into smaller, focused sub-interfaces that drivers can implement selectively.

### Container Operations - Composable Design

```csharp
namespace Ductus.FluentDocker.Drivers.Core.Container
{
    /// <summary>
    /// Main container driver interface - aggregates all container sub-interfaces.
    /// </summary>
    public interface IContainerDriver :
        IContainerLifecycle,
        IContainerInspection,
        IContainerExecution,
        IContainerFiles,
        IContainerLogs,
        IContainerStats,
        IContainerProcesses,
        IContainerHealth
    {
    }

    /// <summary>
    /// Container lifecycle operations (create, start, stop, remove).
    /// </summary>
    public interface IContainerLifecycle
    {
        CommandResponse<string> Create(DriverContext context, ContainerCreateParams createParams);
        CommandResponse<string> Start(DriverContext context, string containerId);
        CommandResponse<string> Stop(DriverContext context, string containerId, int? waitMs = null);
        CommandResponse<string> Restart(DriverContext context, string containerId, int? waitMs = null);
        CommandResponse<string> Pause(DriverContext context, string containerId);
        CommandResponse<string> Unpause(DriverContext context, string containerId);
        CommandResponse<string> Kill(DriverContext context, string containerId, string signal = "SIGKILL");
        CommandResponse<string> Remove(DriverContext context, string containerId, bool force = false, bool removeVolumes = false);
        CommandResponse<string> Rename(DriverContext context, string containerId, string newName);
    }

    /// <summary>
    /// Container inspection and listing.
    /// </summary>
    public interface IContainerInspection
    {
        CommandResponse<Container> Inspect(DriverContext context, string containerId);
        CommandResponse<IList<Container>> List(DriverContext context, bool all = false, ContainerListFilter filter = null);
        CommandResponse<IList<Diff>> Diff(DriverContext context, string containerId);
        CommandResponse<bool> Exists(DriverContext context, string containerId);
        CommandResponse<ContainerState> GetState(DriverContext context, string containerId);
    }

    /// <summary>
    /// Command execution inside containers.
    /// </summary>
    public interface IContainerExecution
    {
        CommandResponse<ExecResult> Execute(DriverContext context, string containerId, ExecParams execParams);
        CommandResponse<string> Attach(DriverContext context, string containerId, AttachParams attachParams);
        CommandResponse<string> CreateExec(DriverContext context, string containerId, ExecCreateParams createParams);
        CommandResponse<ExecResult> StartExec(DriverContext context, string execId, bool detach = false);
        CommandResponse<ExecInfo> InspectExec(DriverContext context, string execId);
    }

    /// <summary>
    /// File operations (copy to/from container).
    /// </summary>
    public interface IContainerFiles
    {
        CommandResponse<string> CopyTo(DriverContext context, string containerId, string hostPath, string containerPath);
        CommandResponse<string> CopyFrom(DriverContext context, string containerId, string containerPath, string hostPath);
        CommandResponse<Stream> GetArchive(DriverContext context, string containerId, string path);
        CommandResponse<string> PutArchive(DriverContext context, string containerId, string path, Stream archive);
    }

    /// <summary>
    /// Container logs.
    /// </summary>
    public interface IContainerLogs
    {
        CommandResponse<string> GetLogs(DriverContext context, string containerId, LogOptions options = null);
        CommandResponse<Stream> StreamLogs(DriverContext context, string containerId, LogOptions options = null);
    }

    /// <summary>
    /// Container statistics.
    /// </summary>
    public interface IContainerStats
    {
        CommandResponse<ContainerStats> GetStats(DriverContext context, string containerId, bool stream = false);
        CommandResponse<Stream> StreamStats(DriverContext context, string containerId);
    }

    /// <summary>
    /// Container process information.
    /// </summary>
    public interface IContainerProcesses
    {
        CommandResponse<Processes> Top(DriverContext context, string containerId, string psArgs = null);
        CommandResponse<IList<Process>> ListProcesses(DriverContext context, string containerId);
    }

    /// <summary>
    /// Container health checks.
    /// </summary>
    public interface IContainerHealth
    {
        CommandResponse<HealthStatus> GetHealth(DriverContext context, string containerId);
        CommandResponse<bool> IsHealthy(DriverContext context, string containerId);
        CommandResponse<string> UpdateHealthCheck(DriverContext context, string containerId, HealthCheckConfig config);
    }
}
```

### Image Operations - Composable Design

```csharp
namespace Ductus.FluentDocker.Drivers.Core.Image
{
    /// <summary>
    /// Main image driver interface - aggregates all image sub-interfaces.
    /// </summary>
    public interface IImageDriver :
        IImageLifecycle,
        IImageBuild,
        IImageRegistry,
        IImageInspection,
        IImageExport
    {
    }

    /// <summary>
    /// Image lifecycle operations.
    /// </summary>
    public interface IImageLifecycle
    {
        CommandResponse<string> Pull(DriverContext context, string image, string tag = "latest", AuthConfig auth = null);
        CommandResponse<string> Remove(DriverContext context, string imageId, bool force = false, bool noPrune = false);
        CommandResponse<string> Tag(DriverContext context, string sourceImage, string targetImage, string tag = "latest");
        CommandResponse<string> Prune(DriverContext context, ImagePruneFilter filter = null);
    }

    /// <summary>
    /// Image building.
    /// </summary>
    public interface IImageBuild
    {
        CommandResponse<IList<string>> Build(DriverContext context, ImageBuildParams buildParams);
        CommandResponse<IList<ImageHistory>> History(DriverContext context, string imageId);
    }

    /// <summary>
    /// Image build with advanced features (BuildX, multi-platform).
    /// </summary>
    public interface IImageBuildAdvanced : IImageBuild
    {
        CommandResponse<IList<string>> BuildMultiPlatform(DriverContext context, BuildXParams buildxParams);
        CommandResponse<string> CreateBuilder(DriverContext context, BuilderCreateParams createParams);
        CommandResponse<IList<Builder>> ListBuilders(DriverContext context);
        CommandResponse<string> InspectBuilder(DriverContext context, string builderName);
        CommandResponse<string> RemoveBuilder(DriverContext context, string builderName);
    }

    /// <summary>
    /// Registry operations (push, search).
    /// </summary>
    public interface IImageRegistry
    {
        CommandResponse<string> Push(DriverContext context, string image, string tag = "latest", AuthConfig auth = null);
        CommandResponse<IList<ImageSearchResult>> Search(DriverContext context, string term, int limit = 25);
    }

    /// <summary>
    /// Image inspection and listing.
    /// </summary>
    public interface IImageInspection
    {
        CommandResponse<ImageConfig> Inspect(DriverContext context, string imageId);
        CommandResponse<IList<DockerImageRowResponse>> List(DriverContext context, bool all = false, ImageListFilter filter = null);
        CommandResponse<bool> Exists(DriverContext context, string imageName);
    }

    /// <summary>
    /// Image import/export.
    /// </summary>
    public interface IImageExport
    {
        CommandResponse<string> Save(DriverContext context, string[] images, string outputPath);
        CommandResponse<string> Load(DriverContext context, string inputPath);
        CommandResponse<Stream> Export(DriverContext context, string[] images);
        CommandResponse<string> Import(DriverContext context, Stream tarStream, string repository, string tag);
    }
}
```

### Network Operations - Composable Design

```csharp
namespace Ductus.FluentDocker.Drivers.Core.Network
{
    /// <summary>
    /// Main network driver interface - aggregates all network sub-interfaces.
    /// </summary>
    public interface INetworkDriver :
        INetworkLifecycle,
        INetworkConnectivity,
        INetworkInspection
    {
    }

    /// <summary>
    /// Network lifecycle operations.
    /// </summary>
    public interface INetworkLifecycle
    {
        CommandResponse<string> Create(DriverContext context, NetworkCreateParams createParams);
        CommandResponse<string> Remove(DriverContext context, string networkId);
        CommandResponse<string> Prune(DriverContext context, NetworkPruneFilter filter = null);
    }

    /// <summary>
    /// Network connectivity (connect/disconnect containers).
    /// </summary>
    public interface INetworkConnectivity
    {
        CommandResponse<string> Connect(DriverContext context, string networkId, string containerId, NetworkConnectParams connectParams = null);
        CommandResponse<string> Disconnect(DriverContext context, string networkId, string containerId, bool force = false);
        CommandResponse<IList<string>> ListConnectedContainers(DriverContext context, string networkId);
    }

    /// <summary>
    /// Network inspection and listing.
    /// </summary>
    public interface INetworkInspection
    {
        CommandResponse<NetworkConfiguration> Inspect(DriverContext context, string networkId);
        CommandResponse<IList<NetworkRow>> List(DriverContext context, NetworkListFilter filter = null);
        CommandResponse<bool> Exists(DriverContext context, string networkId);
    }
}
```

### Volume Operations - Composable Design

```csharp
namespace Ductus.FluentDocker.Drivers.Core.Volume
{
    /// <summary>
    /// Main volume driver interface - aggregates all volume sub-interfaces.
    /// </summary>
    public interface IVolumeDriver :
        IVolumeLifecycle,
        IVolumeInspection
    {
    }

    /// <summary>
    /// Volume lifecycle operations.
    /// </summary>
    public interface IVolumeLifecycle
    {
        CommandResponse<Volume> Create(DriverContext context, VolumeCreateParams createParams);
        CommandResponse<string> Remove(DriverContext context, string volumeName, bool force = false);
        CommandResponse<string> Prune(DriverContext context, VolumePruneFilter filter = null);
    }

    /// <summary>
    /// Volume inspection and listing.
    /// </summary>
    public interface IVolumeInspection
    {
        CommandResponse<Volume> Inspect(DriverContext context, string volumeName);
        CommandResponse<IList<Volume>> List(DriverContext context, VolumeListFilter filter = null);
        CommandResponse<bool> Exists(DriverContext context, string volumeName);
    }
}
```

### Compose Operations - Composable Design

```csharp
namespace Ductus.FluentDocker.Drivers.Core.Compose
{
    /// <summary>
    /// Main compose driver interface - aggregates all compose sub-interfaces.
    /// </summary>
    public interface IComposeDriver :
        IComposeLifecycle,
        IComposeOperations,
        IComposeInspection
    {
    }

    /// <summary>
    /// Compose project lifecycle.
    /// </summary>
    public interface IComposeLifecycle
    {
        CommandResponse<string> Up(DriverContext context, ComposeUpParams upParams);
        CommandResponse<string> Down(DriverContext context, ComposeDownParams downParams);
        CommandResponse<string> Start(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Stop(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Restart(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Pause(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Unpause(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Kill(DriverContext context, ComposeParams composeParams, string signal = "SIGKILL");
        CommandResponse<string> Remove(DriverContext context, ComposeRemoveParams removeParams);
    }

    /// <summary>
    /// Compose operations (build, pull, scale).
    /// </summary>
    public interface IComposeOperations
    {
        CommandResponse<string> Build(DriverContext context, ComposeBuildParams buildParams);
        CommandResponse<string> Pull(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Push(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Scale(DriverContext context, ComposeScaleParams scaleParams);
        CommandResponse<string> Exec(DriverContext context, ComposeExecParams execParams);
        CommandResponse<string> Run(DriverContext context, ComposeRunParams runParams);
    }

    /// <summary>
    /// Compose inspection.
    /// </summary>
    public interface IComposeInspection
    {
        CommandResponse<IList<ComposeContainer>> Ps(DriverContext context, ComposeParams composeParams);
        CommandResponse<DockerComposeConfig> Config(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Logs(DriverContext context, ComposeLogsParams logsParams);
        CommandResponse<PortMapping> Port(DriverContext context, ComposeParams composeParams, string service, int port);
        CommandResponse<string> Version(DriverContext context);
    }
}
```

### System Operations - Composable Design

```csharp
namespace Ductus.FluentDocker.Drivers.Core.System
{
    /// <summary>
    /// Main system driver interface - aggregates all system sub-interfaces.
    /// </summary>
    public interface ISystemDriver :
        ISystemInfo,
        ISystemAuth,
        ISystemEvents,
        ISystemMaintenance
    {
    }

    /// <summary>
    /// System information and version.
    /// </summary>
    public interface ISystemInfo
    {
        CommandResponse<VersionResponse> Version(DriverContext context);
        CommandResponse<SystemInfo> Info(DriverContext context);
        CommandResponse<bool> IsWindowsEngine(DriverContext context);
        CommandResponse<string> Ping(DriverContext context);
    }

    /// <summary>
    /// Registry authentication.
    /// </summary>
    public interface ISystemAuth
    {
        CommandResponse<AuthResult> Login(DriverContext context, AuthConfig auth);
        CommandResponse<string> Logout(DriverContext context, string registry = null);
    }

    /// <summary>
    /// Docker events.
    /// </summary>
    public interface ISystemEvents
    {
        CommandResponse<FdEvent[]> GetEvents(DriverContext context, EventsParams eventsParams);
        CommandResponse<Stream> StreamEvents(DriverContext context, EventsParams eventsParams);
    }

    /// <summary>
    /// System maintenance (disk usage, pruning).
    /// </summary>
    public interface ISystemMaintenance
    {
        CommandResponse<DiskUsage> DiskUsage(DriverContext context);
        CommandResponse<PruneReport> Prune(DriverContext context, PruneOptions options = null);
    }
}
```

### Podman-Specific Interfaces

```csharp
namespace Ductus.FluentDocker.Drivers.Core.Podman
{
    /// <summary>
    /// Podman pod operations (unique to Podman).
    /// </summary>
    public interface IPodDriver :
        IPodLifecycle,
        IPodInspection
    {
    }

    /// <summary>
    /// Pod lifecycle operations.
    /// </summary>
    public interface IPodLifecycle
    {
        CommandResponse<string> Create(DriverContext context, PodCreateParams createParams);
        CommandResponse<string> Start(DriverContext context, string podId);
        CommandResponse<string> Stop(DriverContext context, string podId);
        CommandResponse<string> Restart(DriverContext context, string podId);
        CommandResponse<string> Pause(DriverContext context, string podId);
        CommandResponse<string> Unpause(DriverContext context, string podId);
        CommandResponse<string> Kill(DriverContext context, string podId, string signal = "SIGKILL");
        CommandResponse<string> Remove(DriverContext context, string podId, bool force = false);
        CommandResponse<string> Prune(DriverContext context);
    }

    /// <summary>
    /// Pod inspection and listing.
    /// </summary>
    public interface IPodInspection
    {
        CommandResponse<Pod> Inspect(DriverContext context, string podId);
        CommandResponse<IList<Pod>> List(DriverContext context, PodListFilter filter = null);
        CommandResponse<bool> Exists(DriverContext context, string podId);
        CommandResponse<Processes> Top(DriverContext context, string podId, string psArgs = null);
        CommandResponse<ContainerStats> Stats(DriverContext context, string podId);
    }

    /// <summary>
    /// Kubernetes YAML generation and playback (unique to Podman).
    /// </summary>
    public interface IKubernetesYaml
    {
        CommandResponse<string> Generate(DriverContext context, KubeGenerateParams generateParams);
        CommandResponse<string> Play(DriverContext context, KubePlayParams playParams);
        CommandResponse<string> Down(DriverContext context, string yamlPath);
    }

    /// <summary>
    /// Systemd service generation (unique to Podman).
    /// </summary>
    public interface ISystemdGeneration
    {
        CommandResponse<string> Generate(DriverContext context, SystemdGenerateParams generateParams);
    }
}
```

---

## Enhanced Capability System

### DriverCapabilities Enhancement

```csharp
namespace Ductus.FluentDocker.Drivers.Models
{
    /// <summary>
    /// Comprehensive driver capabilities based on Docker and Podman feature analysis.
    /// </summary>
    public class DriverCapabilities
    {
        // === Core Resource Types ===
        public bool SupportsContainers { get; set; }
        public bool SupportsImages { get; set; }
        public bool SupportsNetworks { get; set; }
        public bool SupportsVolumes { get; set; }

        // === Container Capabilities (Sub-interface support) ===
        public ContainerCapabilities Container { get; set; } = new();

        // === Image Capabilities (Sub-interface support) ===
        public ImageCapabilities Image { get; set; } = new();

        // === Network Capabilities (Sub-interface support) ===
        public NetworkCapabilities Network { get; set; } = new();

        // === Volume Capabilities (Sub-interface support) ===
        public VolumeCapabilities Volume { get; set; } = new();

        // === Compose Support ===
        public ComposeCapabilities Compose { get; set; } = new();

        // === Docker-Specific Features ===
        public DockerSpecificCapabilities DockerSpecific { get; set; } = new();

        // === Podman-Specific Features ===
        public PodmanSpecificCapabilities PodmanSpecific { get; set; } = new();

        // === Security Features ===
        public SecurityCapabilities Security { get; set; } = new();

        // === Performance Features ===
        public PerformanceCapabilities Performance { get; set; } = new();

        // === Runtime Information ===
        public string RuntimeType { get; set; }  // "docker", "podman", etc.
        public string RuntimeVersion { get; set; }
        public string ApiVersion { get; set; }

        /// <summary>
        /// Checks if driver implements a specific interface.
        /// </summary>
        public bool Implements<T>() where T : class
        {
            var interfaceType = typeof(T);

            // Container sub-interfaces
            if (interfaceType == typeof(IContainerLifecycle)) return Container.SupportsLifecycle;
            if (interfaceType == typeof(IContainerInspection)) return Container.SupportsInspection;
            if (interfaceType == typeof(IContainerExecution)) return Container.SupportsExecution;
            if (interfaceType == typeof(IContainerFiles)) return Container.SupportsFileOperations;
            if (interfaceType == typeof(IContainerLogs)) return Container.SupportsLogs;
            if (interfaceType == typeof(IContainerStats)) return Container.SupportsStats;
            if (interfaceType == typeof(IContainerProcesses)) return Container.SupportsProcessInfo;
            if (interfaceType == typeof(IContainerHealth)) return Container.SupportsHealthChecks;

            // Image sub-interfaces
            if (interfaceType == typeof(IImageLifecycle)) return Image.SupportsLifecycle;
            if (interfaceType == typeof(IImageBuild)) return Image.SupportsBuild;
            if (interfaceType == typeof(IImageBuildAdvanced)) return Image.SupportsBuildX;
            if (interfaceType == typeof(IImageRegistry)) return Image.SupportsRegistry;
            if (interfaceType == typeof(IImageInspection)) return Image.SupportsInspection;
            if (interfaceType == typeof(IImageExport)) return Image.SupportsImportExport;

            // Network sub-interfaces
            if (interfaceType == typeof(INetworkLifecycle)) return Network.SupportsLifecycle;
            if (interfaceType == typeof(INetworkConnectivity)) return Network.SupportsConnectivity;
            if (interfaceType == typeof(INetworkInspection)) return Network.SupportsInspection;

            // Volume sub-interfaces
            if (interfaceType == typeof(IVolumeLifecycle)) return Volume.SupportsLifecycle;
            if (interfaceType == typeof(IVolumeInspection)) return Volume.SupportsInspection;

            // Compose sub-interfaces
            if (interfaceType == typeof(IComposeLifecycle)) return Compose.SupportsLifecycle;
            if (interfaceType == typeof(IComposeOperations)) return Compose.SupportsOperations;
            if (interfaceType == typeof(IComposeInspection)) return Compose.SupportsInspection;

            // Podman-specific
            if (interfaceType == typeof(IPodDriver)) return PodmanSpecific.SupportsPods;
            if (interfaceType == typeof(IKubernetesYaml)) return PodmanSpecific.SupportsKubernetesYaml;
            if (interfaceType == typeof(ISystemdGeneration)) return PodmanSpecific.SupportsSystemdGeneration;

            return false;
        }

        /// <summary>
        /// Gets all implemented interfaces.
        /// </summary>
        public IEnumerable<Type> GetImplementedInterfaces()
        {
            var interfaces = new List<Type>();

            if (Container.SupportsLifecycle) interfaces.Add(typeof(IContainerLifecycle));
            if (Container.SupportsInspection) interfaces.Add(typeof(IContainerInspection));
            if (Container.SupportsExecution) interfaces.Add(typeof(IContainerExecution));
            if (Container.SupportsFileOperations) interfaces.Add(typeof(IContainerFiles));
            if (Container.SupportsLogs) interfaces.Add(typeof(IContainerLogs));
            if (Container.SupportsStats) interfaces.Add(typeof(IContainerStats));
            if (Container.SupportsProcessInfo) interfaces.Add(typeof(IContainerProcesses));
            if (Container.SupportsHealthChecks) interfaces.Add(typeof(IContainerHealth));

            // ... add all other interfaces

            return interfaces;
        }
    }

    /// <summary>
    /// Container-specific capabilities.
    /// </summary>
    public class ContainerCapabilities
    {
        // Sub-interface support
        public bool SupportsLifecycle { get; set; } = true;
        public bool SupportsInspection { get; set; } = true;
        public bool SupportsExecution { get; set; } = true;
        public bool SupportsFileOperations { get; set; } = true;
        public bool SupportsLogs { get; set; } = true;
        public bool SupportsStats { get; set; } = true;
        public bool SupportsProcessInfo { get; set; } = true;
        public bool SupportsHealthChecks { get; set; } = true;

        // Fine-grained capabilities
        public bool SupportsAttach { get; set; } = true;
        public bool SupportsExecCreate { get; set; } = true;
        public bool SupportsRename { get; set; } = true;
        public bool SupportsUpdate { get; set; } = true;
        public bool SupportsWait { get; set; } = true;
        public bool SupportsArchiveOperations { get; set; } = true;
        public bool SupportsStreamingLogs { get; set; } = false;
        public bool SupportsStreamingStats { get; set; } = false;
    }

    /// <summary>
    /// Image-specific capabilities.
    /// </summary>
    public class ImageCapabilities
    {
        // Sub-interface support
        public bool SupportsLifecycle { get; set; } = true;
        public bool SupportsBuild { get; set; } = true;
        public bool SupportsBuildX { get; set; } = false;
        public bool SupportsRegistry { get; set; } = true;
        public bool SupportsInspection { get; set; } = true;
        public bool SupportsImportExport { get; set; } = true;

        // Fine-grained capabilities
        public bool SupportsMultiPlatformBuild { get; set; } = false;
        public bool SupportsBuildCache { get; set; } = true;
        public bool SupportsImagePrune { get; set; } = true;
        public bool SupportsImageHistory { get; set; } = true;
        public bool SupportsImageSearch { get; set; } = true;
        public bool SupportsContentTrust { get; set; } = false;
    }

    /// <summary>
    /// Network-specific capabilities.
    /// </summary>
    public class NetworkCapabilities
    {
        // Sub-interface support
        public bool SupportsLifecycle { get; set; } = true;
        public bool SupportsConnectivity { get; set; } = true;
        public bool SupportsInspection { get; set; } = true;

        // Fine-grained capabilities
        public bool SupportsCustomDrivers { get; set; } = true;
        public bool SupportsIPAM { get; set; } = true;
        public bool SupportsIPv6 { get; set; } = true;
        public bool SupportsOverlay { get; set; } = true;
        public bool SupportsMacvlan { get; set; } = true;
        public bool SupportsNetworkPrune { get; set; } = true;
    }

    /// <summary>
    /// Volume-specific capabilities.
    /// </summary>
    public class VolumeCapabilities
    {
        // Sub-interface support
        public bool SupportsLifecycle { get; set; } = true;
        public bool SupportsInspection { get; set; } = true;

        // Fine-grained capabilities
        public bool SupportsCustomDrivers { get; set; } = true;
        public bool SupportsVolumePrune { get; set; } = true;
        public bool SupportsLabels { get; set; } = true;
    }

    /// <summary>
    /// Compose-specific capabilities.
    /// </summary>
    public class ComposeCapabilities
    {
        // Sub-interface support
        public bool SupportsLifecycle { get; set; } = false;
        public bool SupportsOperations { get; set; } = false;
        public bool SupportsInspection { get; set; } = false;

        // Version support
        public bool SupportsComposeV1 { get; set; } = false;
        public bool SupportsComposeV2 { get; set; } = false;

        // Fine-grained capabilities
        public bool SupportsProfiles { get; set; } = false;
        public bool SupportsScale { get; set; } = false;
        public bool SupportsConfigValidation { get; set; } = false;
    }

    /// <summary>
    /// Docker-specific capabilities.
    /// </summary>
    public class DockerSpecificCapabilities
    {
        public bool SupportsSwarm { get; set; } = false;
        public bool SupportsSecrets { get; set; } = false;
        public bool SupportsConfigs { get; set; } = false;
        public bool SupportsStacks { get; set; } = false;
        public bool SupportsServices { get; set; } = false;
        public bool SupportsPlugins { get; set; } = false;
        public bool SupportsContentTrust { get; set; } = false;
        public bool SupportsBuildCloud { get; set; } = false;
    }

    /// <summary>
    /// Podman-specific capabilities.
    /// </summary>
    public class PodmanSpecificCapabilities
    {
        public bool SupportsPods { get; set; } = false;
        public bool SupportsKubernetesYaml { get; set; } = false;
        public bool SupportsSystemdGeneration { get; set; } = false;
        public bool SupportsRootless { get; set; } = false;
        public bool SupportsReducedCapabilities { get; set; } = false;
    }

    /// <summary>
    /// Security-related capabilities.
    /// </summary>
    public class SecurityCapabilities
    {
        public bool SupportsRootless { get; set; } = false;
        public bool SupportsSELinux { get; set; } = false;
        public bool SupportsAppArmor { get; set; } = false;
        public bool SupportsSeccomp { get; set; } = false;
        public bool SupportsUserNamespaces { get; set; } = false;
        public int DefaultCapabilityCount { get; set; } = 14;  // Docker: 14, Podman: 11
    }

    /// <summary>
    /// Performance-related capabilities.
    /// </summary>
    public class PerformanceCapabilities
    {
        public bool SupportsStreaming { get; set; } = false;
        public bool SupportsBulkOperations { get; set; } = false;
        public bool SupportsAsyncOperations { get; set; } = false;
        public bool SupportsParallelPull { get; set; } = false;
        public bool SupportsBuildCache { get; set; } = false;
    }
}
```

### Docker CLI Driver Capabilities

```csharp
public class DockerCliDriver : IDriver
{
    public DriverCapabilities GetCapabilities()
    {
        return new DriverCapabilities
        {
            RuntimeType = "docker",
            RuntimeVersion = Version,

            // Container capabilities
            Container = new ContainerCapabilities
            {
                SupportsLifecycle = true,
                SupportsInspection = true,
                SupportsExecution = true,
                SupportsFileOperations = true,
                SupportsLogs = true,
                SupportsStats = true,
                SupportsProcessInfo = true,
                SupportsHealthChecks = true,
                SupportsStreamingLogs = false,  // CLI doesn't support true streaming
                SupportsStreamingStats = false
            },

            // Image capabilities
            Image = new ImageCapabilities
            {
                SupportsLifecycle = true,
                SupportsBuild = true,
                SupportsBuildX = _useBuildx,
                SupportsRegistry = true,
                SupportsInspection = true,
                SupportsImportExport = true,
                SupportsMultiPlatformBuild = _useBuildx,
                SupportsBuildCache = true,
                SupportsContentTrust = true
            },

            // Network capabilities
            Network = new NetworkCapabilities
            {
                SupportsLifecycle = true,
                SupportsConnectivity = true,
                SupportsInspection = true,
                SupportsCustomDrivers = true,
                SupportsIPAM = true,
                SupportsIPv6 = true,
                SupportsOverlay = true,
                SupportsMacvlan = true
            },

            // Volume capabilities
            Volume = new VolumeCapabilities
            {
                SupportsLifecycle = true,
                SupportsInspection = true,
                SupportsCustomDrivers = true,
                SupportsVolumePrune = true
            },

            // Compose capabilities
            Compose = new ComposeCapabilities
            {
                SupportsLifecycle = _supportsCompose,
                SupportsOperations = _supportsCompose,
                SupportsInspection = _supportsCompose,
                SupportsComposeV1 = _resolver.IsDockerComposeAvailable,
                SupportsComposeV2 = _resolver.IsDockerComposeV2Available,
                SupportsProfiles = _resolver.IsDockerComposeV2Available
            },

            // Docker-specific
            DockerSpecific = new DockerSpecificCapabilities
            {
                SupportsSwarm = true,
                SupportsSecrets = true,
                SupportsConfigs = true,
                SupportsStacks = true,
                SupportsServices = true,
                SupportsPlugins = true,
                SupportsContentTrust = true
            },

            // Security
            Security = new SecurityCapabilities
            {
                SupportsRootless = true,  // Docker supports rootless mode
                SupportsSELinux = true,
                SupportsAppArmor = true,
                SupportsSeccomp = true,
                SupportsUserNamespaces = true,
                DefaultCapabilityCount = 14
            },

            // Performance
            Performance = new PerformanceCapabilities
            {
                SupportsStreaming = false,  // CLI doesn't stream
                SupportsBulkOperations = false,
                SupportsAsyncOperations = false,
                SupportsBuildCache = true
            }
        };
    }
}
```

### Podman CLI Driver Capabilities

```csharp
public class PodmanCliDriver : IDriver
{
    public DriverCapabilities GetCapabilities()
    {
        return new DriverCapabilities
        {
            RuntimeType = "podman",
            RuntimeVersion = Version,

            // Container capabilities
            Container = new ContainerCapabilities
            {
                SupportsLifecycle = true,
                SupportsInspection = true,
                SupportsExecution = true,
                SupportsFileOperations = true,
                SupportsLogs = true,
                SupportsStats = true,
                SupportsProcessInfo = true,
                SupportsHealthChecks = true,
                SupportsStreamingLogs = false,
                SupportsStreamingStats = false
            },

            // Image capabilities
            Image = new ImageCapabilities
            {
                SupportsLifecycle = true,
                SupportsBuild = true,
                SupportsBuildX = false,  // Podman doesn't support BuildX
                SupportsRegistry = true,
                SupportsInspection = true,
                SupportsImportExport = true,
                SupportsMultiPlatformBuild = true,  // Podman supports multi-arch
                SupportsBuildCache = true
            },

            // Network capabilities
            Network = new NetworkCapabilities
            {
                SupportsLifecycle = true,
                SupportsConnectivity = true,
                SupportsInspection = true,
                SupportsCustomDrivers = true,
                SupportsIPAM = true,
                SupportsIPv6 = true,
                SupportsOverlay = false,  // Podman doesn't support overlay networks
                SupportsMacvlan = true
            },

            // Volume capabilities
            Volume = new VolumeCapabilities
            {
                SupportsLifecycle = true,
                SupportsInspection = true,
                SupportsCustomDrivers = true,
                SupportsVolumePrune = true
            },

            // Compose capabilities
            Compose = new ComposeCapabilities
            {
                SupportsLifecycle = _supportsPodmanCompose,
                SupportsOperations = _supportsPodmanCompose,
                SupportsInspection = _supportsPodmanCompose,
                SupportsComposeV1 = false,
                SupportsComposeV2 = _supportsPodmanCompose  // Via podman-compose
            },

            // Podman-specific
            PodmanSpecific = new PodmanSpecificCapabilities
            {
                SupportsPods = true,
                SupportsKubernetesYaml = true,
                SupportsSystemdGeneration = true,
                SupportsRootless = true,
                SupportsReducedCapabilities = true
            },

            // Security
            Security = new SecurityCapabilities
            {
                SupportsRootless = true,  // Rootless by default
                SupportsSELinux = true,
                SupportsAppArmor = true,
                SupportsSeccomp = true,
                SupportsUserNamespaces = true,
                DefaultCapabilityCount = 11  // Podman uses 11 vs Docker's 14
            },

            // Performance
            Performance = new PerformanceCapabilities
            {
                SupportsStreaming = false,
                SupportsBulkOperations = false,
                SupportsAsyncOperations = false,
                SupportsBuildCache = true
            }
        };
    }
}
```

### Usage: Capability Discovery

```csharp
// Check if driver supports specific interface
var driver = kernel.GetDriver("docker");
var caps = driver.GetCapabilities();

if (caps.Implements<IImageBuildAdvanced>())
{
    var buildDriver = driver.Images as IImageBuildAdvanced;
    // Use BuildX features
    buildDriver.BuildMultiPlatform(context, buildxParams);
}

// Check fine-grained capability
if (caps.Container.SupportsHealthChecks)
{
    var containerDriver = driver.Containers as IContainerHealth;
    var health = containerDriver.GetHealth(context, containerId);
}

// List all implemented interfaces
foreach (var iface in caps.GetImplementedInterfaces())
{
    Console.WriteLine($"Implements: {iface.Name}");
}

// Check Podman-specific features
if (caps.PodmanSpecific.SupportsPods)
{
    var podDriver = (driver as IPodDriverProvider)?.Pods;
    podDriver.Create(context, podCreateParams);
}

// Check security capabilities
Console.WriteLine($"Rootless support: {caps.Security.SupportsRootless}");
Console.WriteLine($"Default capabilities: {caps.Security.DefaultCapabilityCount}");
```

---

## Removing Fd.XXX Static Methods

### Current Fd Class Usage

The `Fd` class in `Common/Fd.cs` contains static helper methods:

```csharp
public static class Fd
{
    internal static void DisposeOnException<T>(Action<T> action, T service, string name = null)
        where T : IService
    {
        try
        {
            action.Invoke(service);
        }
        catch
        {
            Logger.Log($"Failed to run action for {name} disposing service {service.Name}");
            service.Dispose();
            throw;
        }
    }
}
```

### Migration Strategy

**Remove static methods** and provide instance-based alternatives:

1. **Move to ServiceExtensions** - Create extension methods on IService
2. **Use try-finally** - Explicit error handling
3. **Use using statements** - Automatic disposal

### Migration Examples

**v2.x.x - Using Fd.DisposeOnException:**
```csharp
Fd.DisposeOnException(svc =>
{
    var result = container.DockerHost.Execute(container.Id, command);
    if (!result.Success)
        throw new FluentDockerException($"Failed: {result.Error}");
}, service, "Execute Command");
```

**v3.0.0 - Option 1: Extension Method:**
```csharp
// ServiceExtensions.cs
public static class ServiceExtensions
{
    public static void ExecuteWithDisposal<T>(this T service, Action<T> action, string operationName = null)
        where T : IService
    {
        try
        {
            action.Invoke(service);
        }
        catch (Exception ex)
        {
            LoggerFactory.GetLogger().LogError(
                $"Failed to run '{operationName}' on service {service.Name}",
                service.Context,
                ex
            );
            service.Dispose();
            throw;
        }
    }
}

// Usage
service.ExecuteWithDisposal(svc =>
{
    var result = container.Kernel.SysCtl<IContainerDriver>(container.DriverId)
        .Execute(container.Context, container.Id, command);

    if (!result.Success)
        throw new ContainerExecutionException(container.Id, result.Error);
}, "Execute Command");
```

**v3.0.0 - Option 2: Try-Finally:**
```csharp
IContainerService container = null;
try
{
    container = new Builder(kernel)
        .UseContainer()
        .UseImage("nginx")
        .Build();

    container.Start();

    // Do work
}
catch (Exception ex)
{
    Logger.LogError($"Container operation failed: {ex.Message}");
    container?.Dispose();
    throw;
}
```

**v3.0.0 - Option 3: Using Statement (Recommended):**
```csharp
using (var container = new Builder(kernel)
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start())
{
    // Do work - automatic disposal on exception or completion
}
```

### Migration Guide for Fd.XXX Removal

**Replace all Fd.DisposeOnException calls:**

1. **Identify usage**: Search for `Fd.DisposeOnException` in codebase
2. **Replace with using**: Prefer using statements for cleaner code
3. **Use extensions**: For complex scenarios, use ServiceExtensions
4. **Update documentation**: Remove references to Fd class

---

## Updated Error Handling for New Capabilities

### New Exceptions for Composable Interfaces

```csharp
namespace Ductus.FluentDocker.Exceptions
{
    /// <summary>
    /// Exception thrown when a driver doesn't support a required sub-interface.
    /// </summary>
    public class InterfaceNotSupportedException : DriverException
    {
        public Type InterfaceType { get; set; }

        public InterfaceNotSupportedException(string driverId, Type interfaceType)
            : base($"Driver '{driverId}' does not support interface '{interfaceType.Name}'", driverId)
        {
            InterfaceType = interfaceType;
            ErrorCode = ErrorCodes.Driver.InterfaceNotSupported;
        }
    }

    /// <summary>
    /// Exception thrown when a capability is not supported.
    /// </summary>
    public class CapabilityNotSupportedException : DriverException
    {
        public string CapabilityName { get; set; }

        public CapabilityNotSupportedException(string driverId, string capabilityName)
            : base($"Driver '{driverId}' does not support capability '{capabilityName}'", driverId)
        {
            CapabilityName = capabilityName;
            ErrorCode = ErrorCodes.Driver.CapabilityNotSupported;
        }
    }
}
```

### Error Codes Update

```csharp
public static class ErrorCodes
{
    public static class Driver
    {
        // ... existing codes
        public const string InterfaceNotSupported = "DRIVER.INTERFACE_NOT_SUPPORTED";
        public const string CapabilityNotSupported = "DRIVER.CAPABILITY_NOT_SUPPORTED";
    }
}
```

### Safe Interface Access

```csharp
public static class DriverExtensions
{
    /// <summary>
    /// Safely gets a sub-interface from a driver, throwing if not supported.
    /// </summary>
    public static T GetInterface<T>(this IDriver driver, string driverId) where T : class
    {
        var caps = driver.GetCapabilities();

        if (!caps.Implements<T>())
        {
            throw new InterfaceNotSupportedException(driverId, typeof(T));
        }

        // Get the appropriate component
        if (typeof(T) == typeof(IContainerLifecycle)) return driver.Containers as T;
        if (typeof(T) == typeof(IImageBuildAdvanced)) return driver.Images as T;
        // ... other interfaces

        throw new InterfaceNotSupportedException(driverId, typeof(T));
    }

    /// <summary>
    /// Tries to get a sub-interface from a driver, returning null if not supported.
    /// </summary>
    public static T TryGetInterface<T>(this IDriver driver) where T : class
    {
        var caps = driver.GetCapabilities();

        if (!caps.Implements<T>())
            return null;

        // Get the appropriate component
        if (typeof(T) == typeof(IContainerLifecycle)) return driver.Containers as T;
        if (typeof(T) == typeof(IImageBuildAdvanced)) return driver.Images as T;
        // ... other interfaces

        return null;
    }
}

// Usage
var driver = kernel.GetDriver("docker");

// Throws if not supported
var buildAdvanced = driver.GetInterface<IImageBuildAdvanced>("docker");
buildAdvanced.BuildMultiPlatform(context, buildxParams);

// Returns null if not supported
var buildAdvanced2 = driver.TryGetInterface<IImageBuildAdvanced>();
if (buildAdvanced2 != null)
{
    buildAdvanced2.BuildMultiPlatform(context, buildxParams);
}
```

---

## Summary

### Key Enhancements

✅ **Fluent API** - Intuitive driver registration and kernel configuration
✅ **Composable Interfaces** - 30+ sub-interfaces for fine-grained implementation
✅ **Enhanced Capabilities** - Comprehensive capability system with 100+ flags
✅ **Feature Detection** - `Implements<T>()` method for interface support checking
✅ **Docker Features** - Swarm, secrets, configs, BuildX, content trust
✅ **Podman Features** - Pods, Kubernetes YAML, systemd, rootless, reduced caps
✅ **Security Capabilities** - Rootless, SELinux, AppArmor, capability counts
✅ **Performance Flags** - Streaming, bulk operations, async support
✅ **Fd.XXX Removal** - Migration to extension methods and using statements
✅ **Updated Error Handling** - New exceptions for capability/interface checks

### Benefits

**For Users:**
- Intuitive fluent configuration
- Better capability discovery
- Safer interface access
- Clearer error messages

**For Driver Implementers:**
- Implement only needed sub-interfaces
- Fine-grained capability declaration
- Clear contract definitions
- Easier testing (mock specific interfaces)

**For Maintainers:**
- Better separation of concerns
- Easier to add new features
- Clear capability matrix
- Comprehensive feature detection

The enhanced architecture provides **enterprise-grade flexibility** while maintaining **clean, intuitive APIs** for users!
