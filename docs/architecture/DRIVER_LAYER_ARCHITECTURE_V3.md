# FluentDocker v3.0.0 - Driver Layer Architecture

## Executive Summary

This document describes the architecture for FluentDocker v3.0.0, introducing a **pluggable driver layer** that supports multiple container runtime implementations with multiple concurrent instances.

**Breaking Changes**: This is v3.0.0 - breaking changes are acceptable where they improve the design.

**Goals:**
1. Support multiple container runtime drivers (Docker CLI, Docker API, Podman CLI)
2. Allow multiple driver instances with unique IDs (e.g., multiple Docker hosts)
3. Multiple kernel instances can coexist (no singleton)
4. Fluent API binds to specific kernel instances
5. Driver access via `SysCtl()` interface pattern
6. Expose unique runtime features cleanly

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

### Key Statistics

- **239 C# files** in main project
- **71 ProcessExecutor usages** across Commands layer
- **100% CLI-based** - No Docker API usage currently
- **22 response parsers** for CLI output

---

## Proposed v3.0.0 Architecture

### Overview

```
┌────────────────────────────────────────────────────────────────┐
│         Layer 3: Fluent API (Builders)                         │
│              Binds to Kernel Instance                          │
└────────────────────────────────────────────────────────────────┘
                            ↓
┌────────────────────────────────────────────────────────────────┐
│         Layer 2: Services (Domain Objects)                     │
│              References Kernel Instance                        │
└────────────────────────────────────────────────────────────────┘
                            ↓
┌────────────────────────────────────────────────────────────────┐
│         FluentDocker Kernel (Instantiable)                     │
│  ┌──────────────────────────────────────────────────────┐      │
│  │  DriverRegistry   DriverSelector   DriverRouter      │      │
│  │  SysCtl() Interface for Driver Access                │      │
│  └──────────────────────────────────────────────────────┘      │
└────────────────────────────────────────────────────────────────┘
                            ↓
┌────────────────────────────────────────────────────────────────┐
│         Driver Layer (Multiple Instances)                      │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐            │
│  │  dc-1       │  │  dc-2       │  │  podman-1   │            │
│  │ Docker CLI  │  │ Docker API  │  │ Podman CLI  │            │
│  │ localhost   │  │ remote:2376 │  │ rootless    │            │
│  └─────────────┘  └─────────────┘  └─────────────┘            │
└────────────────────────────────────────────────────────────────┘
```

### Key Concepts

#### 1. Multiple Kernel Instances

**No Singleton** - Kernels are instantiated explicitly:

```csharp
// Create kernel for local Docker
var localKernel = new FluentDockerKernel();
localKernel.RegisterDriver("docker-local", new DockerCliDriver(
    new DockerUri("unix:///var/run/docker.sock")
));

// Create kernel for remote Docker
var remoteKernel = new FluentDockerKernel();
remoteKernel.RegisterDriver("docker-remote", new DockerApiDriver(
    new DockerUri("tcp://remote-host:2376"),
    certificates
));

// Both kernels operate independently
```

**Benefits**:
- Multiple Docker hosts simultaneously
- Different driver configurations per kernel
- Better testing (isolated kernels)
- Explicit lifecycle management
- No global state

#### 2. SysCtl() Driver Access

**Pattern**: Access drivers via system control interface inspired by Unix sysctl:

```csharp
// Access specific driver's network interface
var networkDriver = kernel.SysCtl("docker-local", DriverComponent.Network);
var networks = networkDriver.List(context);

// Access container interface
var containerDriver = kernel.SysCtl("podman-1", DriverComponent.Container);
var containers = containerDriver.List(context);

// Type-safe access
var networkDriver = kernel.SysCtl<INetworkDriver>("docker-local");
```

**Signature**:
```csharp
public interface IFluentDockerKernel
{
    // Get specific driver component
    IDriverComponent SysCtl(string driverId, DriverComponent component);

    // Generic type-safe access
    T SysCtl<T>(string driverId) where T : class;

    // Get entire driver
    IDriver GetDriver(string driverId);
}
```

#### 3. Multiple Driver Instances

**Same driver type, different instances**:

```csharp
var kernel = new FluentDockerKernel();

// Register multiple Docker CLI drivers
kernel.RegisterDriver("docker-local", new DockerCliDriver(
    new DockerUri("unix:///var/run/docker.sock")
));

kernel.RegisterDriver("docker-remote-1", new DockerCliDriver(
    new DockerUri("tcp://host1:2376"),
    certificates1
));

kernel.RegisterDriver("docker-remote-2", new DockerCliDriver(
    new DockerUri("tcp://host2:2376"),
    certificates2
));

// Register Podman drivers
kernel.RegisterDriver("podman-rootless", new PodmanCliDriver(
    rootless: true
));

kernel.RegisterDriver("podman-system", new PodmanCliDriver(
    rootless: false
));

// Use specific driver
var containers = kernel.SysCtl("docker-remote-1", DriverComponent.Container)
    .List(new DriverContext());
```

#### 4. Fluent API Kernel Binding

**Builders bind to kernel instance**:

```csharp
// Create kernel
var kernel = new FluentDockerKernel();
kernel.RegisterDriver("docker-local", new DockerCliDriver());

// Fluent API binds to kernel
using var container = new Builder(kernel)
    .UseContainer()
    .UseImage("nginx")
    .UseDriver("docker-local")  // Optional: specify driver
    .Build()
    .Start();

// Or use default driver
using var container = new Builder(kernel)
    .UseContainer()
    .UseImage("nginx")
    .Build()  // Uses default driver
    .Start();
```

**Breaking Change**: `new Builder()` now requires kernel parameter (or uses default).

---

## Core Components

### 1. FluentDockerKernel

**No longer a singleton** - Instantiable class:

```csharp
public class FluentDockerKernel : IFluentDockerKernel, IDisposable
{
    private readonly IDriverRegistry _registry;
    private readonly IDriverSelector _selector;
    private readonly DriverRouter _router;

    // Constructor - no more singleton
    public FluentDockerKernel(FluentDockerKernelOptions options = null)
    {
        _registry = new DriverRegistry();
        _selector = new DefaultDriverSelector(_registry);
        _router = new DriverRouter(_selector);

        // Optional auto-registration
        if (options?.AutoRegisterDrivers ?? true)
        {
            AutoRegisterDrivers();
        }
    }

    // SysCtl() interface for driver access
    public IDriverComponent SysCtl(string driverId, DriverComponent component)
    {
        var driver = _registry.GetDriver(driverId);
        if (driver == null)
            throw new DriverNotFoundException($"Driver '{driverId}' not found");

        return component switch
        {
            DriverComponent.Container => driver.Containers,
            DriverComponent.Image => driver.Images,
            DriverComponent.Network => driver.Networks,
            DriverComponent.Volume => driver.Volumes,
            DriverComponent.Compose => driver.Compose,
            DriverComponent.System => driver.System,
            _ => throw new ArgumentException($"Unknown component: {component}")
        };
    }

    // Generic type-safe access
    public T SysCtl<T>(string driverId) where T : class
    {
        var driver = _registry.GetDriver(driverId);
        if (driver == null)
            throw new DriverNotFoundException($"Driver '{driverId}' not found");

        // Map type to component
        if (typeof(T) == typeof(IContainerDriver)) return driver.Containers as T;
        if (typeof(T) == typeof(IImageDriver)) return driver.Images as T;
        if (typeof(T) == typeof(INetworkDriver)) return driver.Networks as T;
        if (typeof(T) == typeof(IVolumeDriver)) return driver.Volumes as T;
        if (typeof(T) == typeof(IComposeDriver)) return driver.Compose as T;
        if (typeof(T) == typeof(ISystemDriver)) return driver.System as T;

        throw new ArgumentException($"Unknown driver component type: {typeof(T).Name}");
    }

    // Get entire driver
    public IDriver GetDriver(string driverId)
    {
        return _registry.GetDriver(driverId);
    }

    // Register driver with ID
    public void RegisterDriver(string driverId, IDriver driver, DriverRegistrationOptions options = null)
    {
        _registry.Register(driverId, driver, options);
    }

    // Unregister driver
    public void UnregisterDriver(string driverId)
    {
        _registry.Unregister(driverId);
    }

    // Get all registered driver IDs
    public IEnumerable<string> GetDriverIds()
    {
        return _registry.GetAllDriverIds();
    }

    // High-level operations using driver selection
    public CommandResponse<string> CreateContainer(ContainerCreateParams createParams,
        DriverContext context = null)
    {
        context ??= new DriverContext();
        var driver = _selector.SelectDriver(context, _registry);
        return driver.Containers.Create(context, createParams);
    }

    // ... other high-level operations

    public void Dispose()
    {
        _registry.Dispose();
    }
}
```

### 2. DriverComponent Enum

```csharp
public enum DriverComponent
{
    Container,
    Image,
    Network,
    Volume,
    Compose,
    System
}
```

### 3. Driver Registry (Updated)

**Registry manages drivers by ID**:

```csharp
public interface IDriverRegistry : IDisposable
{
    // Register driver with unique ID
    void Register(string driverId, IDriver driver, DriverRegistrationOptions options = null);

    // Unregister driver by ID
    void Unregister(string driverId);

    // Get driver by ID
    IDriver GetDriver(string driverId);

    // Get all driver IDs
    IEnumerable<string> GetAllDriverIds();

    // Get all drivers
    IEnumerable<KeyValuePair<string, IDriver>> GetAllDrivers();

    // Get registration options
    DriverRegistrationOptions GetRegistrationOptions(string driverId);

    // Check if driver is registered
    bool IsRegistered(string driverId);

    // Get drivers by type
    IEnumerable<KeyValuePair<string, IDriver>> GetDriversByType(DriverType type);

    // Get drivers by runtime
    IEnumerable<KeyValuePair<string, IDriver>> GetDriversByRuntime(RuntimeType runtime);
}
```

**Implementation**:

```csharp
public class DriverRegistry : IDriverRegistry
{
    private readonly ConcurrentDictionary<string, IDriver> _drivers = new();
    private readonly ConcurrentDictionary<string, DriverRegistrationOptions> _options = new();

    public void Register(string driverId, IDriver driver, DriverRegistrationOptions options = null)
    {
        if (string.IsNullOrWhiteSpace(driverId))
            throw new ArgumentException("Driver ID cannot be null or empty", nameof(driverId));

        if (driver == null)
            throw new ArgumentNullException(nameof(driver));

        options ??= new DriverRegistrationOptions();

        if (!_drivers.TryAdd(driverId, driver))
        {
            throw new InvalidOperationException($"Driver with ID '{driverId}' is already registered");
        }

        _options[driverId] = options;
    }

    public void Unregister(string driverId)
    {
        if (_drivers.TryRemove(driverId, out var driver))
        {
            _options.TryRemove(driverId, out _);
            driver?.Dispose();
        }
    }

    public IDriver GetDriver(string driverId)
    {
        return _drivers.TryGetValue(driverId, out var driver) ? driver : null;
    }

    public IEnumerable<string> GetAllDriverIds()
    {
        return _drivers.Keys;
    }

    public IEnumerable<KeyValuePair<string, IDriver>> GetAllDrivers()
    {
        return _drivers;
    }

    public DriverRegistrationOptions GetRegistrationOptions(string driverId)
    {
        return _options.TryGetValue(driverId, out var opts) ? opts : null;
    }

    public bool IsRegistered(string driverId)
    {
        return _drivers.ContainsKey(driverId);
    }

    public IEnumerable<KeyValuePair<string, IDriver>> GetDriversByType(DriverType type)
    {
        return _drivers.Where(kvp => kvp.Value.Type == type);
    }

    public IEnumerable<KeyValuePair<string, IDriver>> GetDriversByRuntime(RuntimeType runtime)
    {
        return _drivers.Where(kvp => kvp.Value.Runtime == runtime);
    }

    public void Dispose()
    {
        foreach (var driver in _drivers.Values)
        {
            driver?.Dispose();
        }
        _drivers.Clear();
        _options.Clear();
    }
}
```

### 4. Driver Interfaces (Updated)

**IDriver interface**:

```csharp
public interface IDriver : IDisposable
{
    /// <summary>
    /// Driver type name (e.g., "docker-cli", "docker-api", "podman-cli").
    /// NOT the instance ID - that's managed by the registry.
    /// </summary>
    string DriverTypeName { get; }

    /// <summary>
    /// Driver version.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Type of driver implementation (CLI, API, Hybrid).
    /// </summary>
    DriverType Type { get; }

    /// <summary>
    /// Container runtime type (Docker, Podman, etc.).
    /// </summary>
    RuntimeType Runtime { get; }

    /// <summary>
    /// Driver for container operations.
    /// </summary>
    IContainerDriver Containers { get; }

    /// <summary>
    /// Driver for image operations.
    /// </summary>
    IImageDriver Images { get; }

    /// <summary>
    /// Driver for network operations.
    /// </summary>
    INetworkDriver Networks { get; }

    /// <summary>
    /// Driver for volume operations.
    /// </summary>
    IVolumeDriver Volumes { get; }

    /// <summary>
    /// Driver for compose operations.
    /// </summary>
    IComposeDriver Compose { get; }

    /// <summary>
    /// Driver for system operations.
    /// </summary>
    ISystemDriver System { get; }

    /// <summary>
    /// Gets the capabilities supported by this driver.
    /// </summary>
    DriverCapabilities GetCapabilities();

    /// <summary>
    /// Checks if the driver is available in the given context.
    /// </summary>
    bool IsAvailable(DriverContext context);

    /// <summary>
    /// Performs a health check on the driver.
    /// </summary>
    DriverHealthStatus HealthCheck(DriverContext context);
}
```

**Note**: Driver instances don't know their ID - that's registry responsibility.

### 5. DriverContext (Updated)

**Include driver ID preference**:

```csharp
public class DriverContext
{
    /// <summary>
    /// Docker/container host URI.
    /// </summary>
    public DockerUri Host { get; set; }

    /// <summary>
    /// TLS certificate paths.
    /// </summary>
    public ICertificatePaths Certificates { get; set; }

    /// <summary>
    /// Sudo mechanism for privileged operations.
    /// </summary>
    public SudoMechanism SudoMechanism { get; set; } = SudoMechanism.None;

    /// <summary>
    /// Operation timeout.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Driver selection preferences.
    /// </summary>
    public DriverPreferences Preferences { get; set; }

    /// <summary>
    /// Cancellation token.
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

    public DriverContext()
    {
        Preferences = new DriverPreferences();
    }
}

public class DriverPreferences
{
    /// <summary>
    /// Preferred driver ID to use (e.g., "docker-local", "podman-1").
    /// If specified, this driver will be used if available.
    /// </summary>
    public string PreferredDriverId { get; set; }

    /// <summary>
    /// Preferred driver type (CLI, API, etc.).
    /// </summary>
    public PreferredDriverType PreferredType { get; set; } = PreferredDriverType.Auto;

    /// <summary>
    /// Allow fallback to another driver if preferred is unavailable.
    /// </summary>
    public bool AllowFallback { get; set; } = true;

    /// <summary>
    /// Target container runtime.
    /// </summary>
    public RuntimeType TargetRuntime { get; set; } = RuntimeType.Auto;

    /// <summary>
    /// Ordered list of preferred driver IDs.
    /// </summary>
    public IList<string> PreferredDriverIds { get; set; } = new List<string>();

    /// <summary>
    /// Minimum priority for driver selection.
    /// </summary>
    public int MinimumPriority { get; set; } = 0;
}
```

---

## Usage Examples

### Example 1: Single Local Docker

```csharp
// Create kernel
var kernel = new FluentDockerKernel();
kernel.RegisterDriver("docker", new DockerCliDriver());

// Use fluent API
using var container = new Builder(kernel)
    .UseContainer()
    .UseImage("nginx:alpine")
    .WithName("my-nginx")
    .ExposePort(80)
    .Build()
    .Start();

// Direct driver access via SysCtl
var containerDriver = kernel.SysCtl<IContainerDriver>("docker");
var logs = containerDriver.Logs(new DriverContext(), container.Id);
```

### Example 2: Multiple Docker Hosts

```csharp
// Create kernel with multiple Docker instances
var kernel = new FluentDockerKernel(new FluentDockerKernelOptions
{
    AutoRegisterDrivers = false  // Manual registration
});

// Register local Docker CLI
kernel.RegisterDriver("local", new DockerCliDriver(
    new DockerUri("unix:///var/run/docker.sock")
));

// Register remote Docker API
kernel.RegisterDriver("remote-prod", new DockerApiDriver(
    new DockerUri("tcp://prod.example.com:2376"),
    prodCertificates
));

// Register remote Docker API (staging)
kernel.RegisterDriver("remote-staging", new DockerApiDriver(
    new DockerUri("tcp://staging.example.com:2376"),
    stagingCertificates
));

// Deploy to production
using var prodContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("remote-prod")
    .UseImage("myapp:latest")
    .Build()
    .Start();

// Deploy to staging
using var stagingContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("remote-staging")
    .UseImage("myapp:latest")
    .Build()
    .Start();

// Check both via SysCtl
var prodNetworks = kernel.SysCtl("remote-prod", DriverComponent.Network)
    .List(new DriverContext());

var stagingNetworks = kernel.SysCtl("remote-staging", DriverComponent.Network)
    .List(new DriverContext());
```

### Example 3: Docker + Podman Simultaneously

```csharp
var kernel = new FluentDockerKernel(autoRegister: false);

// Register Docker drivers
kernel.RegisterDriver("docker-cli", new DockerCliDriver());
kernel.RegisterDriver("docker-api", new DockerApiDriver());

// Register Podman drivers
kernel.RegisterDriver("podman-rootless", new PodmanCliDriver(rootless: true));
kernel.RegisterDriver("podman-system", new PodmanCliDriver(rootless: false));

// Create Docker container
using var dockerContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("docker-cli")
    .UseImage("nginx")
    .Build()
    .Start();

// Create Podman container
using var podmanContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("podman-rootless")
    .UseImage("nginx")
    .Build()
    .Start();

// Use Podman-specific features via SysCtl
var podmanDriver = kernel.GetDriver("podman-rootless") as PodmanCliDriver;
if (podmanDriver != null)
{
    // Access Podman pods feature
    var podDriver = podmanDriver.Pods;
    var pod = podDriver.Create(new DriverContext(), new PodCreateParams
    {
        Name = "my-pod"
    });
}
```

### Example 4: Driver Selection with Context

```csharp
var kernel = new FluentDockerKernel();
kernel.RegisterDriver("docker-cli", new DockerCliDriver());
kernel.RegisterDriver("docker-api", new DockerApiDriver());

// Automatic selection based on preferences
var context = new DriverContext
{
    Preferences = new DriverPreferences
    {
        PreferredDriverIds = new List<string> { "docker-api", "docker-cli" },
        AllowFallback = true
    }
};

// Will try docker-api first, fallback to docker-cli if unavailable
var container = kernel.CreateContainer(new ContainerCreateParams
{
    Image = "nginx"
}, context);
```

### Example 5: Testing with Mock Drivers

```csharp
[Fact]
public void Should_Create_Container_On_Mock_Driver()
{
    // Create kernel with mock driver
    var kernel = new FluentDockerKernel(autoRegister: false);
    var mockDriver = new MockDockerDriver();
    kernel.RegisterDriver("mock", mockDriver);

    // Use fluent API with mock
    var builder = new Builder(kernel)
        .UseContainer()
        .UseDriver("mock")
        .UseImage("test-image")
        .Build();

    // Verify mock was called
    Assert.Contains("test-image", mockDriver.CreatedContainers);
}
```

---

## Fluent API Changes

### Builder Changes (Breaking)

**v2.x.x**:
```csharp
// Old way - no kernel parameter
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();
```

**v3.0.0**:
```csharp
// New way - requires kernel
var kernel = new FluentDockerKernel();

using var container = new Builder(kernel)
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();

// Or specify driver explicitly
using var container = new Builder(kernel)
    .UseContainer()
    .UseDriver("docker-api")  // NEW: Specify driver ID
    .UseImage("nginx")
    .Build()
    .Start();
```

**Default Kernel Option**:

For convenience, provide a default kernel:

```csharp
public static class FluentDocker
{
    private static Lazy<FluentDockerKernel> _defaultKernel =
        new Lazy<FluentDockerKernel>(() => new FluentDockerKernel());

    public static FluentDockerKernel DefaultKernel => _defaultKernel.Value;

    public static void ResetDefaultKernel()
    {
        if (_defaultKernel.IsValueCreated)
        {
            _defaultKernel.Value.Dispose();
        }
        _defaultKernel = new Lazy<FluentDockerKernel>(() => new FluentDockerKernel());
    }
}

// Usage with default kernel
using var container = new Builder(FluentDocker.DefaultKernel)
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();

// Or even simpler - Builder uses default if not specified
using var container = new Builder()  // Uses FluentDocker.DefaultKernel
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();
```

### Builder Implementation

```csharp
public class Builder : BaseBuilder<ICompositeService>
{
    private readonly FluentDockerKernel _kernel;

    // Constructor with explicit kernel
    public Builder(FluentDockerKernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    // Constructor using default kernel (convenience)
    public Builder() : this(FluentDocker.DefaultKernel)
    {
    }

    public FluentDockerKernel Kernel => _kernel;

    public HostBuilder UseHost()
    {
        return new HostBuilder(this, _kernel);
    }

    public ContainerBuilder UseContainer()
    {
        return new ContainerBuilder(this, _kernel);
    }

    public NetworkBuilder UseNetwork(string name = null)
    {
        return new NetworkBuilder(this, _kernel, name);
    }

    // ... other builders
}
```

### ContainerBuilder with Driver Selection

```csharp
public class ContainerBuilder : BaseBuilder<IContainerService>
{
    private readonly FluentDockerKernel _kernel;
    private readonly ContainerBuilderConfig _config;
    private string _driverId;  // NEW: Specific driver ID

    public ContainerBuilder(IBuilder parent, FluentDockerKernel kernel)
        : base(parent)
    {
        _kernel = kernel;
        _config = new ContainerBuilderConfig();
    }

    // NEW: Specify driver ID
    public ContainerBuilder UseDriver(string driverId)
    {
        _driverId = driverId;
        return this;
    }

    public ContainerBuilder UseImage(string image)
    {
        _config.Image = image;
        return this;
    }

    public override IContainerService Build()
    {
        // Create context with driver preference
        var context = new DriverContext();
        if (!string.IsNullOrEmpty(_driverId))
        {
            context.Preferences.PreferredDriverId = _driverId;
        }

        // Create container using kernel
        var createParams = _config.ToCreateParams();
        var response = _kernel.CreateContainer(createParams, context);

        if (!response.Success)
            throw new FluentDockerException($"Failed to create container: {response.Error}");

        // Return service bound to kernel
        return new DockerContainerService(response.Data, _config.Image, _kernel, context);
    }
}
```

---

## Services Layer Changes

### IContainerService Changes

**v2.x.x**:
```csharp
public interface IContainerService : IService
{
    DockerUri DockerHost { get; }
    ICertificatePaths Certificates { get; }
    // ...
}
```

**v3.0.0**:
```csharp
public interface IContainerService : IService
{
    FluentDockerKernel Kernel { get; }
    string DriverId { get; }  // Which driver manages this container
    DriverContext Context { get; }
    // DockerHost and Certificates moved to Context
    // ...
}
```

### DockerContainerService Implementation

```csharp
public class DockerContainerService : ServiceBase, IContainerService
{
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;
    private readonly DriverContext _context;

    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;
    public DriverContext Context => _context;

    public DockerContainerService(
        string id,
        string image,
        FluentDockerKernel kernel,
        DriverContext context)
    {
        Id = id;
        Image = image;
        _kernel = kernel;
        _context = context;

        // Determine which driver was used
        _driverId = context.Preferences?.PreferredDriverId
            ?? kernel.GetDriverIds().FirstOrDefault();
    }

    public override void Start()
    {
        if (State == ServiceRunningState.Running)
            return;

        var response = _kernel.SysCtl<IContainerDriver>(_driverId)
            .Start(_context, Id);

        if (!response.Success)
            throw new FluentDockerException($"Failed to start container: {response.Error}");

        State = ServiceRunningState.Running;
        OnStateChange(ServiceRunningState.Running);
    }

    public override void Stop()
    {
        if (State == ServiceRunningState.Stopped)
            return;

        var response = _kernel.SysCtl<IContainerDriver>(_driverId)
            .Stop(_context, Id);

        if (!response.Success)
            throw new FluentDockerException($"Failed to stop container: {response.Error}");

        State = ServiceRunningState.Stopped;
        OnStateChange(ServiceRunningState.Stopped);
    }

    public Container GetConfiguration(bool fresh = false)
    {
        var response = _kernel.SysCtl<IContainerDriver>(_driverId)
            .Inspect(_context, Id);

        if (!response.Success)
            throw new FluentDockerException($"Failed to inspect container: {response.Error}");

        return response.Data;
    }
}
```

---

## Migration Path for Users

### Breaking Changes Summary

1. **Builder requires kernel parameter** (or uses default)
2. **Services reference kernel instead of DockerUri**
3. **DriverContext replaces DockerUri + ICertificatePaths in many APIs**
4. **Some Commands layer methods may change signatures**

### Migration Examples

**v2.x.x Code**:
```csharp
// Simple container
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();

// Custom host
var host = new DockerUri("tcp://remote:2376");
var certs = new CertificatePaths("path/to/certs");
using var container = new Builder()
    .UseHost()
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();
```

**v3.0.0 Migration**:

**Option 1: Use default kernel (minimal changes)**:
```csharp
// No changes needed if using default
using var container = new Builder()  // Uses FluentDocker.DefaultKernel
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();
```

**Option 2: Explicit kernel**:
```csharp
// Create kernel explicitly
var kernel = new FluentDockerKernel();

using var container = new Builder(kernel)
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();
```

**Option 3: Custom host/driver**:
```csharp
// Create kernel with custom driver
var kernel = new FluentDockerKernel(autoRegister: false);
kernel.RegisterDriver("remote", new DockerApiDriver(
    new DockerUri("tcp://remote:2376"),
    new CertificatePaths("path/to/certs")
));

using var container = new Builder(kernel)
    .UseContainer()
    .UseDriver("remote")
    .UseImage("nginx")
    .Build()
    .Start();
```

---

## Summary

The v3.0.0 architecture provides:

✅ **Multiple kernel instances** - No singleton, full control over lifecycle
✅ **SysCtl() driver access** - Clean, discoverable driver interface
✅ **Multiple driver instances** - Same type, different configurations
✅ **Fluent API kernel binding** - Explicit kernel parameter
✅ **Breaking changes acceptable** - v3.0.0 allows improvements
✅ **Flexible driver registration** - Manual or automatic
✅ **Better testing** - Isolated kernels, mock drivers
✅ **Multi-host support** - Multiple Docker/Podman hosts simultaneously

The design is cleaner, more testable, and more flexible than the singleton approach while maintaining the elegant fluent API that users love.
