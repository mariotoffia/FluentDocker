# FluentDocker Driver Layer Architecture

## Executive Summary

This document describes the architecture and design for refactoring FluentDocker to introduce a **pluggable driver layer** that supports multiple container runtime implementations (Docker CLI, Docker API, Podman CLI, etc.) while maintaining backward compatibility with the existing fluent API.

**Goals:**
1. Support multiple container runtime drivers (Docker CLI, Docker API, Podman CLI)
2. Allow multiple drivers to coexist and be used simultaneously
3. Automatically handle differences between runtimes
4. Expose unique features through namespace inclusion or dynamic API extensions
5. Maintain 100% backward compatibility with existing code

---

## Current Architecture Analysis

### Three-Layer Architecture

```
┌─────────────────────────────────────────────┐
│         Layer 3: Fluent API                 │
│  (Builders - ContainerBuilder, etc.)        │
└─────────────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────────────┐
│         Layer 2: Services                   │
│  (DockerHostService, ContainerService)      │
└─────────────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────────────┐
│         Layer 1: Commands                   │
│  (Client.cs, Compose.cs, Network.cs)        │
└─────────────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────────────┐
│         ProcessExecutor<T>                  │
│  (Shell out to docker/docker-compose)       │
└─────────────────────────────────────────────┘
```

### Key Characteristics

- **100% CLI-based**: All operations use `ProcessExecutor` to execute Docker binaries
- **71 ProcessExecutor usages** across Commands layer
- **Well-defined service interfaces**: IHostService, IContainerService, INetworkService, etc.
- **Rich domain models**: Container, Image, Network, Volume, Compose models
- **22 response parsers** for CLI output (JSON/text parsing)

### Pain Points

1. **Tight coupling to Docker CLI**: Cannot use Docker API/SDK
2. **No Podman support**: Architecture assumes Docker binaries
3. **Performance**: Shelling out to processes is slower than API calls
4. **Limited extensibility**: Hard to add new runtimes
5. **Testing challenges**: Must mock process execution

---

## Proposed Driver Architecture

### Overview

Introduce a **driver layer** between Services and Commands that abstracts container runtime operations. The new architecture:

```
┌──────────────────────────────────────────────────────────────┐
│              Layer 3: Fluent API (Builders)                  │
│         ContainerBuilder, ImageBuilder, etc.                 │
└──────────────────────────────────────────────────────────────┘
                            ↓
┌──────────────────────────────────────────────────────────────┐
│         Layer 2: Services (Domain Objects)                   │
│    DockerHostService, ContainerService, NetworkService       │
└──────────────────────────────────────────────────────────────┘
                            ↓
┌──────────────────────────────────────────────────────────────┐
│         NEW: FluentDocker Kernel                             │
│  ┌────────────────────────────────────────────────────┐      │
│  │  DriverRegistry    DriverSelector    DriverRouter  │      │
│  │  CapabilityMatrix  FeatureDiscovery  EventBus      │      │
│  └────────────────────────────────────────────────────┘      │
└──────────────────────────────────────────────────────────────┘
                            ↓
┌──────────────────────────────────────────────────────────────┐
│         NEW: Driver Layer (Pluggable Implementations)        │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐          │
│  │ Docker CLI  │  │ Docker API  │  │ Podman CLI  │          │
│  │   Driver    │  │   Driver    │  │   Driver    │          │
│  └─────────────┘  └─────────────┘  └─────────────┘          │
│                                                               │
│  Each driver implements:                                     │
│  - IContainerDriver  - INetworkDriver                        │
│  - IImageDriver      - IVolumeDriver                         │
│  - IComposeDriver    - ISystemDriver                         │
└──────────────────────────────────────────────────────────────┘
                            ↓
┌──────────────────────────────────────────────────────────────┐
│         Execution Layer                                      │
│  ProcessExecutor (CLI) | HttpClient (API) | LibPodApi        │
└──────────────────────────────────────────────────────────────┘
```

### Key Components

#### 1. FluentDocker Kernel

The **kernel** is the central coordination layer that:

- **Manages driver lifecycle**: Registration, initialization, disposal
- **Routes operations**: Selects appropriate driver based on context
- **Handles multi-driver scenarios**: Coordinates operations across drivers
- **Provides capability discovery**: Exposes what each driver supports
- **Manages events**: Unified event system across drivers
- **Caches driver instances**: Performance optimization

**Core Classes:**
- `FluentDockerKernel` - Main kernel singleton
- `DriverRegistry` - Register and discover drivers
- `DriverSelector` - Select driver based on host, capabilities, preferences
- `DriverRouter` - Route operations to correct driver
- `CapabilityMatrix` - Track feature support per driver
- `DriverContext` - Execution context (host, certificates, preferences)

#### 2. Driver Interfaces

**Base Driver Interface:**

```csharp
public interface IDriver : IDisposable
{
    string Name { get; }
    string Version { get; }
    DriverType Type { get; }  // CLI, API, Hybrid
    RuntimeType Runtime { get; }  // Docker, Podman, Containerd

    DriverCapabilities GetCapabilities();
    bool IsAvailable(DriverContext context);
    DriverHealthStatus HealthCheck(DriverContext context);

    IContainerDriver Containers { get; }
    IImageDriver Images { get; }
    INetworkDriver Networks { get; }
    IVolumeDriver Volumes { get; }
    IComposeDriver Compose { get; }
    ISystemDriver System { get; }
}
```

**Specialized Driver Interfaces:**

```csharp
public interface IContainerDriver
{
    // Container lifecycle
    CommandResponse<string> Create(DriverContext context, ContainerCreateParams createParams);
    CommandResponse<string> Start(DriverContext context, string containerId);
    CommandResponse<string> Stop(DriverContext context, string containerId, TimeSpan? timeout = null);
    CommandResponse<string> Pause(DriverContext context, string containerId);
    CommandResponse<string> Unpause(DriverContext context, string containerId);
    CommandResponse<string> Remove(DriverContext context, string containerId, bool force = false, bool removeVolumes = false);
    CommandResponse<string> Restart(DriverContext context, string containerId, TimeSpan? timeout = null);
    CommandResponse<string> Kill(DriverContext context, string containerId, string signal = "SIGKILL");

    // Container inspection
    CommandResponse<Container> Inspect(DriverContext context, string containerId);
    CommandResponse<IList<Container>> List(DriverContext context, bool all = false, ContainerListFilter filter = null);
    CommandResponse<Processes> Top(DriverContext context, string containerId, string psArgs = null);
    CommandResponse<IList<Diff>> Diff(DriverContext context, string containerId);
    CommandResponse<ContainerStats> Stats(DriverContext context, string containerId, bool stream = false);
    CommandResponse<string> Logs(DriverContext context, string containerId, LogOptions options = null);

    // Container execution
    CommandResponse<ExecResult> Execute(DriverContext context, string containerId, ExecParams execParams);
    CommandResponse<string> Attach(DriverContext context, string containerId, AttachParams attachParams);

    // Container file operations
    CommandResponse<string> CopyTo(DriverContext context, string containerId, string localPath, string containerPath);
    CommandResponse<string> CopyFrom(DriverContext context, string containerId, string containerPath, string localPath);

    // Container resource management
    CommandResponse<string> Update(DriverContext context, string containerId, ContainerUpdateParams updateParams);
    CommandResponse<string> Rename(DriverContext context, string containerId, string newName);
}

public interface IImageDriver
{
    CommandResponse<string> Pull(DriverContext context, string image, string tag = "latest", AuthConfig auth = null);
    CommandResponse<string> Push(DriverContext context, string image, string tag = "latest", AuthConfig auth = null);
    CommandResponse<string> Build(DriverContext context, ImageBuildParams buildParams);
    CommandResponse<IList<DockerImageRowResponse>> List(DriverContext context, bool all = false, ImageListFilter filter = null);
    CommandResponse<ImageConfig> Inspect(DriverContext context, string imageId);
    CommandResponse<IList<string>> Remove(DriverContext context, string imageId, bool force = false, bool noPrune = false);
    CommandResponse<string> Tag(DriverContext context, string sourceImage, string targetImage, string tag = "latest");
    CommandResponse<string> Save(DriverContext context, string[] images, string outputPath);
    CommandResponse<string> Load(DriverContext context, string inputPath);
    CommandResponse<IList<ImageHistory>> History(DriverContext context, string imageId);
    CommandResponse<string> Prune(DriverContext context, ImagePruneFilter filter = null);
}

public interface INetworkDriver
{
    CommandResponse<string> Create(DriverContext context, NetworkCreateParams createParams);
    CommandResponse<NetworkConfiguration> Inspect(DriverContext context, string networkId);
    CommandResponse<IList<NetworkRow>> List(DriverContext context, NetworkListFilter filter = null);
    CommandResponse<string> Remove(DriverContext context, string networkId);
    CommandResponse<string> Connect(DriverContext context, string networkId, string containerId, NetworkConnectParams connectParams = null);
    CommandResponse<string> Disconnect(DriverContext context, string networkId, string containerId, bool force = false);
    CommandResponse<string> Prune(DriverContext context, NetworkPruneFilter filter = null);
}

public interface IVolumeDriver
{
    CommandResponse<string> Create(DriverContext context, VolumeCreateParams createParams);
    CommandResponse<Volume> Inspect(DriverContext context, string volumeName);
    CommandResponse<IList<Volume>> List(DriverContext context, VolumeListFilter filter = null);
    CommandResponse<string> Remove(DriverContext context, string volumeName, bool force = false);
    CommandResponse<string> Prune(DriverContext context, VolumePruneFilter filter = null);
}

public interface IComposeDriver
{
    CommandResponse<string> Up(DriverContext context, ComposeUpParams upParams);
    CommandResponse<string> Down(DriverContext context, ComposeDownParams downParams);
    CommandResponse<string> Start(DriverContext context, ComposeParams composeParams);
    CommandResponse<string> Stop(DriverContext context, ComposeParams composeParams);
    CommandResponse<string> Restart(DriverContext context, ComposeParams composeParams);
    CommandResponse<string> Pause(DriverContext context, ComposeParams composeParams);
    CommandResponse<string> Unpause(DriverContext context, ComposeParams composeParams);
    CommandResponse<string> Build(DriverContext context, ComposeBuildParams buildParams);
    CommandResponse<string> Pull(DriverContext context, ComposeParams composeParams);
    CommandResponse<IList<ComposeContainer>> Ps(DriverContext context, ComposeParams composeParams);
    CommandResponse<string> Logs(DriverContext context, ComposeLogsParams logsParams);
    CommandResponse<string> Kill(DriverContext context, ComposeParams composeParams, string signal = "SIGKILL");
    CommandResponse<string> Remove(DriverContext context, ComposeRemoveParams removeParams);
    CommandResponse<string> Scale(DriverContext context, ComposeScaleParams scaleParams);
    CommandResponse<DockerComposeConfig> Config(DriverContext context, ComposeParams composeParams);
    CommandResponse<string> Version(DriverContext context);
}

public interface ISystemDriver
{
    CommandResponse<VersionResponse> Version(DriverContext context);
    CommandResponse<SystemInfo> Info(DriverContext context);
    CommandResponse<DiskUsage> DiskUsage(DriverContext context);
    CommandResponse<string> Ping(DriverContext context);
    CommandResponse<AuthResult> Login(DriverContext context, AuthConfig auth);
    CommandResponse<string> Logout(DriverContext context, string registry = null);
    CommandResponse<FdEvent[]> Events(DriverContext context, EventsParams eventsParams);
}
```

#### 3. Driver Context

**DriverContext** carries execution context for operations:

```csharp
public class DriverContext
{
    public DockerUri Host { get; set; }
    public ICertificatePaths Certificates { get; set; }
    public SudoMechanism SudoMechanism { get; set; }
    public TimeSpan? Timeout { get; set; }
    public DriverPreferences Preferences { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public IDictionary<string, object> Metadata { get; set; }
}

public class DriverPreferences
{
    public PreferredDriverType PreferredType { get; set; }  // CLI, API, Auto
    public bool AllowFallback { get; set; }  // Fall back to another driver if preferred unavailable
    public RuntimeType TargetRuntime { get; set; }  // Docker, Podman, Auto
    public IList<string> PreferredDrivers { get; set; }  // Ordered list of driver names
}
```

#### 4. Capability System

**DriverCapabilities** defines what each driver supports:

```csharp
public class DriverCapabilities
{
    public bool SupportsContainers { get; set; }
    public bool SupportsImages { get; set; }
    public bool SupportsNetworks { get; set; }
    public bool SupportsVolumes { get; set; }
    public bool SupportsCompose { get; set; }
    public bool SupportsSwarm { get; set; }
    public bool SupportsSecrets { get; set; }
    public bool SupportsConfigs { get; set; }
    public bool SupportsPlugins { get; set; }

    // Advanced features
    public bool SupportsHealthCheck { get; set; }
    public bool SupportsMultiPlatformBuild { get; set; }
    public bool SupportsBuildx { get; set; }
    public bool SupportsContentTrust { get; set; }
    public bool SupportsRootless { get; set; }

    // Runtime-specific
    public bool SupportsPods { get; set; }  // Podman
    public bool SupportsKubeYaml { get; set; }  // Podman

    // Performance
    public bool SupportsStreaming { get; set; }
    public bool SupportsBulkOperations { get; set; }

    // Version constraints
    public string MinimumRuntimeVersion { get; set; }
    public string MaximumRuntimeVersion { get; set; }

    public ISet<string> CustomCapabilities { get; set; }
}
```

#### 5. Driver Registry

**DriverRegistry** manages driver lifecycle:

```csharp
public interface IDriverRegistry
{
    void Register(IDriver driver, DriverRegistrationOptions options = null);
    void Unregister(string driverName);
    IDriver GetDriver(string name);
    IEnumerable<IDriver> GetAllDrivers();
    IEnumerable<IDriver> GetAvailableDrivers(DriverContext context);
    IEnumerable<IDriver> GetDriversByType(DriverType type);
    IEnumerable<IDriver> GetDriversByRuntime(RuntimeType runtime);
    IDriver GetDefaultDriver(DriverContext context);
}

public class DriverRegistrationOptions
{
    public int Priority { get; set; }  // Higher priority drivers selected first
    public bool IsDefault { get; set; }
    public bool AutoInitialize { get; set; }
    public Func<DriverContext, bool> AvailabilityChecker { get; set; }
}
```

#### 6. Driver Selector

**DriverSelector** chooses the best driver for a given operation:

```csharp
public interface IDriverSelector
{
    IDriver SelectDriver(DriverContext context, DriverSelectionCriteria criteria = null);
}

public class DriverSelectionCriteria
{
    public RuntimeType? RequiredRuntime { get; set; }
    public DriverType? PreferredType { get; set; }
    public ISet<string> RequiredCapabilities { get; set; }
    public Func<IDriver, int> ScoringFunction { get; set; }
}
```

---

## Driver Implementations

### 1. Docker CLI Driver

**Purpose**: Migrate existing CLI-based operations

**Implementation**:
```csharp
public class DockerCliDriver : IDriver
{
    private readonly DockerBinariesResolver _resolver;

    public string Name => "docker-cli";
    public string Version { get; }
    public DriverType Type => DriverType.CLI;
    public RuntimeType Runtime => RuntimeType.Docker;

    public IContainerDriver Containers => new DockerCliContainerDriver(this);
    public IImageDriver Images => new DockerCliImageDriver(this);
    public INetworkDriver Networks => new DockerCliNetworkDriver(this);
    public IVolumeDriver Volumes => new DockerCliVolumeDriver(this);
    public IComposeDriver Compose => new DockerComposeCliDriver(this);
    public ISystemDriver System => new DockerCliSystemDriver(this);

    public DriverCapabilities GetCapabilities()
    {
        return new DriverCapabilities
        {
            SupportsContainers = true,
            SupportsImages = true,
            SupportsNetworks = true,
            SupportsVolumes = true,
            SupportsCompose = true,
            SupportsSwarm = true,
            // ... all Docker features
        };
    }

    public bool IsAvailable(DriverContext context)
    {
        return _resolver.IsDockerAvailable;
    }
}
```

**Migration**: Move code from `Commands/Client.cs`, `Commands/Network.cs`, etc. to driver implementations.

### 2. Docker API Driver

**Purpose**: Use Docker Engine API for better performance

**Implementation**:
```csharp
public class DockerApiDriver : IDriver
{
    private readonly Docker.DotNet.DockerClient _client;

    public string Name => "docker-api";
    public string Version { get; }
    public DriverType Type => DriverType.API;
    public RuntimeType Runtime => RuntimeType.Docker;

    public IContainerDriver Containers => new DockerApiContainerDriver(_client);
    public IImageDriver Images => new DockerApiImageDriver(_client);
    // ... other drivers

    public bool IsAvailable(DriverContext context)
    {
        try
        {
            var uri = context.Host.ToUri();
            _client = new DockerClientConfiguration(uri).CreateClient();
            var version = _client.System.GetVersionAsync().Result;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

**Dependencies**: Use `Docker.DotNet` NuGet package

### 3. Podman CLI Driver

**Purpose**: Support Podman container runtime

**Implementation**:
```csharp
public class PodmanCliDriver : IDriver
{
    public string Name => "podman-cli";
    public string Version { get; }
    public DriverType Type => DriverType.CLI;
    public RuntimeType Runtime => RuntimeType.Podman;

    public IContainerDriver Containers => new PodmanCliContainerDriver(this);
    public IImageDriver Images => new PodmanCliImageDriver(this);
    // ... Podman-specific implementations

    public DriverCapabilities GetCapabilities()
    {
        return new DriverCapabilities
        {
            SupportsContainers = true,
            SupportsImages = true,
            SupportsNetworks = true,
            SupportsVolumes = true,
            SupportsCompose = true,  // Via podman-compose
            SupportsPods = true,  // Unique to Podman
            SupportsKubeYaml = true,  // Unique to Podman
            SupportsRootless = true,
            // ...
        };
    }
}
```

**Unique Features**: Exposed via extensions or namespaces (see below)

---

## Kernel Design

### FluentDockerKernel

The kernel is a singleton that coordinates all driver operations:

```csharp
public class FluentDockerKernel : IDisposable
{
    private static readonly Lazy<FluentDockerKernel> _instance =
        new Lazy<FluentDockerKernel>(() => new FluentDockerKernel());

    public static FluentDockerKernel Instance => _instance.Value;

    private readonly IDriverRegistry _registry;
    private readonly IDriverSelector _selector;
    private readonly DriverRouter _router;
    private readonly EventBus _eventBus;

    public IDriverRegistry Registry => _registry;
    public IDriverSelector Selector => _selector;

    private FluentDockerKernel()
    {
        _registry = new DriverRegistry();
        _selector = new DefaultDriverSelector(_registry);
        _router = new DriverRouter(_selector);
        _eventBus = new EventBus();

        // Auto-register built-in drivers
        AutoRegisterDrivers();
    }

    private void AutoRegisterDrivers()
    {
        // Try to register Docker CLI driver (existing implementation)
        if (new DockerBinariesResolver(SudoMechanism.None, null).IsDockerAvailable)
        {
            _registry.Register(new DockerCliDriver(), new DriverRegistrationOptions
            {
                Priority = 100,
                IsDefault = true,
                AutoInitialize = true
            });
        }

        // Try to register Docker API driver
        try
        {
            var apiDriver = new DockerApiDriver();
            if (apiDriver.IsAvailable(new DriverContext()))
            {
                _registry.Register(apiDriver, new DriverRegistrationOptions
                {
                    Priority = 200,  // Prefer API over CLI for performance
                    AutoInitialize = true
                });
            }
        }
        catch { /* Driver not available */ }

        // Try to register Podman CLI driver
        if (IsPodmanAvailable())
        {
            _registry.Register(new PodmanCliDriver(), new DriverRegistrationOptions
            {
                Priority = 50,
                AutoInitialize = true
            });
        }
    }

    // High-level operations
    public CommandResponse<string> CreateContainer(ContainerCreateParams createParams, DriverContext context = null)
    {
        context ??= new DriverContext();
        var driver = _selector.SelectDriver(context);
        return driver.Containers.Create(context, createParams);
    }

    // ... other operations

    public void Dispose()
    {
        foreach (var driver in _registry.GetAllDrivers())
        {
            driver.Dispose();
        }
    }
}
```

### Driver Router

The router handles complex multi-driver scenarios:

```csharp
public class DriverRouter
{
    private readonly IDriverSelector _selector;

    public DriverRouter(IDriverSelector selector)
    {
        _selector = selector;
    }

    // Route operation to appropriate driver
    public CommandResponse<T> Route<T>(
        DriverContext context,
        Func<IDriver, CommandResponse<T>> operation,
        DriverSelectionCriteria criteria = null)
    {
        var driver = _selector.SelectDriver(context, criteria);

        try
        {
            return operation(driver);
        }
        catch (Exception ex) when (context.Preferences?.AllowFallback == true)
        {
            // Try fallback driver
            var fallbackDriver = GetFallbackDriver(driver, context);
            if (fallbackDriver != null)
            {
                return operation(fallbackDriver);
            }
            throw;
        }
    }

    // Coordinate operations across multiple drivers
    public IEnumerable<CommandResponse<T>> Scatter<T>(
        IEnumerable<DriverContext> contexts,
        Func<IDriver, DriverContext, CommandResponse<T>> operation)
    {
        return contexts.Select(ctx =>
        {
            var driver = _selector.SelectDriver(ctx);
            return operation(driver, ctx);
        });
    }
}
```

---

## Multi-Driver Support

### Use Case: Managing Docker and Podman Simultaneously

```csharp
// Create containers on different runtimes
var dockerContext = new DriverContext
{
    Preferences = new DriverPreferences { TargetRuntime = RuntimeType.Docker }
};

var podmanContext = new DriverContext
{
    Preferences = new DriverPreferences { TargetRuntime = RuntimeType.Podman }
};

var kernel = FluentDockerKernel.Instance;

var dockerContainer = kernel.CreateContainer(new ContainerCreateParams { ... }, dockerContext);
var podmanContainer = kernel.CreateContainer(new ContainerCreateParams { ... }, podmanContext);
```

### Use Case: Automatic Fallback

```csharp
var context = new DriverContext
{
    Preferences = new DriverPreferences
    {
        PreferredType = PreferredDriverType.API,  // Try API first
        AllowFallback = true  // Fall back to CLI if API unavailable
    }
};

var container = kernel.CreateContainer(createParams, context);
// Will use Docker API driver if available, otherwise Docker CLI driver
```

---

## Unique Feature Exposure

### Approach 1: Extension Methods (Recommended)

Create namespace-specific extensions for unique features:

```csharp
namespace Ductus.FluentDocker.Extensions.Podman
{
    public static class PodmanExtensions
    {
        // Expose Podman pods feature
        public static IPodService CreatePod(this IHostService host, PodCreateParams createParams)
        {
            var kernel = FluentDockerKernel.Instance;
            var context = new DriverContext { Host = host.Host };

            var driver = kernel.Registry.GetDriver("podman-cli") as PodmanCliDriver;
            if (driver == null)
                throw new NotSupportedException("Podman driver not available");

            var result = driver.Pods.Create(context, createParams);
            return new PodmanPodService(result.Data, host);
        }

        // Generate Kubernetes YAML from Podman
        public static string GenerateKubeYaml(this IContainerService container)
        {
            var driver = GetPodmanDriver();
            return driver.Kube.Generate(container.Id);
        }
    }
}
```

**Usage**:
```csharp
using Ductus.FluentDocker.Extensions.Podman;

var pod = hostService.CreatePod(new PodCreateParams { Name = "my-pod" });
var yaml = container.GenerateKubeYaml();
```

### Approach 2: Dynamic Capabilities

Use capability discovery to expose features:

```csharp
public static class DriverExtensions
{
    public static bool SupportsPods(this IDriver driver)
    {
        return driver.GetCapabilities().SupportsPods;
    }

    public static IPodDriver GetPodDriver(this IDriver driver)
    {
        if (!driver.SupportsPods())
            throw new NotSupportedException($"Driver {driver.Name} does not support pods");

        return (driver as IPodDriverProvider)?.Pods;
    }
}
```

### Approach 3: Fluent API Extensions

Extend builders for driver-specific features:

```csharp
public static class PodmanBuilderExtensions
{
    public static PodBuilder UsePod(this Builder builder)
    {
        return new PodBuilder(builder);
    }
}

public class PodBuilder : BaseBuilder<IPodService>
{
    public PodBuilder WithInfraContainer(bool enabled) { ... }
    public PodBuilder AddContainer(ContainerBuilder container) { ... }
    public PodBuilder ShareNetwork(bool share) { ... }

    public override IPodService Build()
    {
        // Create pod using Podman driver
    }
}
```

**Usage**:
```csharp
using Ductus.FluentDocker.Builders.Podman;

var pod = new Builder()
    .UsePod()
    .WithInfraContainer(true)
    .ShareNetwork(true)
    .AddContainer(new Builder().UseContainer().UseImage("nginx"))
    .Build();
```

---

## Backward Compatibility Strategy

### 1. Keep Commands Layer as Facade

The existing `Commands` layer remains but delegates to drivers:

```csharp
// Before (current):
public static CommandResponse<string> Start(this DockerUri host, string containerId)
{
    return new ProcessExecutor<...>(...).Execute();
}

// After (with drivers):
public static CommandResponse<string> Start(this DockerUri host, string containerId)
{
    var context = new DriverContext { Host = host };
    var driver = FluentDockerKernel.Instance.Selector.SelectDriver(context);
    return driver.Containers.Start(context, containerId);
}
```

### 2. Services Use Kernel

Services layer uses kernel instead of Commands:

```csharp
// Before:
public override void Start()
{
    DockerHost.Start(Id);
    State = ServiceRunningState.Running;
}

// After:
public override void Start()
{
    var context = new DriverContext { Host = DockerHost };
    FluentDockerKernel.Instance.CreateContainer(..., context);
    State = ServiceRunningState.Running;
}
```

### 3. Builders Unchanged

Builders remain 100% compatible - they use Services which use Kernel.

### 4. Default Driver Selection

Without explicit configuration, FluentDocker automatically selects the best available driver:

```csharp
// User code remains unchanged
using (var container = new Builder()
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start())
{
    // Works with Docker CLI, Docker API, or Podman - automatic selection
}
```

---

## Performance Optimizations

### 1. Driver Caching

```csharp
public class DriverRegistry
{
    private readonly ConcurrentDictionary<string, IDriver> _drivers = new();
    private readonly ConcurrentDictionary<DriverContext, IDriver> _contextCache = new();

    public IDriver GetDriver(DriverContext context)
    {
        return _contextCache.GetOrAdd(context, ctx => SelectBestDriver(ctx));
    }
}
```

### 2. Lazy Driver Initialization

Drivers are only initialized when first used.

### 3. Batch Operations

Drivers can implement batch operations for performance:

```csharp
public interface IContainerDriver
{
    // Single operation
    CommandResponse<string> Start(DriverContext context, string containerId);

    // Batch operation
    CommandResponse<IList<string>> StartMultiple(DriverContext context, IEnumerable<string> containerIds);
}
```

### 4. Streaming Support

API-based drivers can stream logs/stats:

```csharp
public interface IContainerDriver
{
    IAsyncEnumerable<string> StreamLogs(DriverContext context, string containerId, LogOptions options);
    IAsyncEnumerable<ContainerStats> StreamStats(DriverContext context, string containerId);
}
```

---

## Testing Strategy

### 1. Driver Interface Tests

Abstract test suite that all drivers must pass:

```csharp
public abstract class DriverTestSuite
{
    protected abstract IDriver GetDriver();

    [Fact]
    public void CreateContainer_ShouldReturnContainerId()
    {
        var driver = GetDriver();
        var result = driver.Containers.Create(...);
        Assert.True(result.Success);
        Assert.NotEmpty(result.Data);
    }

    // ... more tests
}

public class DockerCliDriverTests : DriverTestSuite
{
    protected override IDriver GetDriver() => new DockerCliDriver();
}

public class DockerApiDriverTests : DriverTestSuite
{
    protected override IDriver GetDriver() => new DockerApiDriver();
}
```

### 2. Mock Drivers

Create mock drivers for unit testing:

```csharp
public class MockDriver : IDriver
{
    public Dictionary<string, object> RecordedCalls { get; } = new();

    public CommandResponse<string> Create(...)
    {
        RecordedCalls["Create"] = createParams;
        return new CommandResponse<string> { Success = true, Data = "mock-id" };
    }
}
```

### 3. Integration Tests

Test multi-driver scenarios:

```csharp
[Fact]
public void Should_Support_Docker_And_Podman_Simultaneously()
{
    var kernel = FluentDockerKernel.Instance;

    var dockerCtx = new DriverContext { Preferences = new() { TargetRuntime = RuntimeType.Docker } };
    var podmanCtx = new DriverContext { Preferences = new() { TargetRuntime = RuntimeType.Podman } };

    var dockerContainer = kernel.CreateContainer(..., dockerCtx);
    var podmanContainer = kernel.CreateContainer(..., podmanCtx);

    Assert.NotEqual(dockerContainer.Data, podmanContainer.Data);
}
```

---

## Migration Path

### Phase 1: Infrastructure (Weeks 1-2)
- Implement driver interfaces
- Implement FluentDockerKernel
- Implement DriverRegistry, DriverSelector, DriverRouter
- Create DriverContext and DriverCapabilities

### Phase 2: Docker CLI Driver (Weeks 3-4)
- Migrate Commands/Client.cs to DockerCliContainerDriver
- Migrate Commands/Network.cs to DockerCliNetworkDriver
- Migrate Commands/Images.cs to DockerCliImageDriver
- Migrate Commands/Volumes.cs to DockerCliVolumeDriver
- Migrate Commands/Compose.cs to DockerComposeCliDriver
- Update Services to use Kernel

### Phase 3: Docker API Driver (Weeks 5-6)
- Implement DockerApiDriver using Docker.DotNet
- Implement all IXxxDriver interfaces
- Add API-specific optimizations (streaming, batch)
- Test against Docker daemon

### Phase 4: Podman CLI Driver (Weeks 7-8)
- Implement PodmanCliDriver
- Handle Podman-specific differences
- Implement unique features (pods, kube YAML)
- Create Podman extensions

### Phase 5: Testing & Documentation (Weeks 9-10)
- Complete driver test suite
- Integration tests
- Update documentation
- Migration guide for users
- Performance benchmarking

---

## Summary

The proposed driver architecture:

✅ **Pluggable**: Support Docker CLI, Docker API, Podman CLI, future runtimes
✅ **Multi-driver**: Manage multiple runtimes simultaneously
✅ **Automatic**: Intelligent driver selection based on context
✅ **Extensible**: Unique features via namespaces/extensions
✅ **Compatible**: 100% backward compatibility with existing code
✅ **Performant**: API drivers for performance, CLI for compatibility
✅ **Testable**: Mock drivers, comprehensive test suite
✅ **Maintainable**: Clean separation of concerns, SOLID principles

The kernel acts as a **coordination layer** that routes operations to appropriate drivers while maintaining the elegant fluent API that users love.
