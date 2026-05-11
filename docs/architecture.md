---
layout: default
title: Architecture
nav_order: 13
description: "FluentDocker v3.0 architecture - Driver layer, kernel configuration, async patterns"
---

# FluentDocker v3.0 Architecture

This document describes the v3.0 architecture with the pluggable driver layer, kernel configuration, and async patterns.

## Step by Step

This is an advanced guide. If you are new to FluentDocker, complete [Getting Started](getting-started.html) first.

- Foundation: [Overview](#overview), [Async Pattern](#async-pattern), [Kernel Configuration](#kernel-configuration)
- Advanced internals: [SysCtl() Driver Access](#sysctl-driver-access), [Scoped Builder Pattern](#scoped-builder-pattern), [Driver-Aware Builder Extensions](#driver-aware-builder-extensions), [Capabilities System](#capabilities-system)
- Design rationale: [Key Architecture Decisions](#key-architecture-decisions)

## Overview

FluentDocker v3.0 introduces a **pluggable driver architecture** that supports multiple container runtime implementations with concurrent instances.

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
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐             │
│  │  dc-1       │  │  dc-2       │  │  podman-1   │             │
│  │ Docker CLI  │  │ Docker API  │  │ Podman CLI  │             │
│  │ localhost   │  │ remote:2376 │  │ rootless    │             │
│  └─────────────┘  └─────────────┘  └─────────────┘             │
└────────────────────────────────────────────────────────────────┘
```

**Key Goals:**
1. Support multiple container runtime drivers (Docker CLI, Docker API, Podman CLI)
2. Allow multiple driver instances with unique IDs (e.g., multiple Docker hosts)
3. Multiple kernel instances can coexist (no singleton)
4. Fluent API binds to specific kernel instances
5. Driver access via `SysCtl()` interface pattern

---

## Async Pattern

**All operations in FluentDocker v3.0 are asynchronous.** The `BuildAsync()` method is terminal and returns `Task<TResult>`.

### Terminal BuildAsync() Pattern

```csharp
// Kernel - BuildAsync() returns Task<FluentDockerKernel>
var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => { })
    .BuildAsync();  // TERMINAL ASYNC

// Container - BuildAsync() returns Task<BuildResults>
var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c.UseImage("nginx"))
    .BuildAsync();  // TERMINAL ASYNC

// Service operations are async
await results.All[0].StartAsync();
await results.DisposeAllAsync();
```

### Key Changes from v2

| v2 (Sync) | v3 (Async) |
|-----------|------------|
| `.Build()` | `await .BuildAsync()` |
| `.Start()` | `await .StartAsync()` |
| `.Stop()` | `await .StopAsync()` |
| `.Remove()` | `await .RemoveAsync()` |

### CancellationToken Support

All async methods accept `CancellationToken`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => { })
    .BuildAsync(cts.Token);

var deployment = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c.UseImage("nginx"))
    .BuildAsync(cts.Token);
```

---

## Kernel Configuration

### Fluent Kernel Builder

```csharp
var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker-local", d => d
        .AtHost("unix:///var/run/docker.sock"))
    .WithDockerApi("docker-remote", d => d
        .AtHost("tcp://remote:2376")
        .WithCertificates("/path/to/certs")
        .AsDefault())
    .WithPodmanCli("podman", d => { })
    .BuildAsync();
```

### Multiple Kernel Instances

Kernels are instantiable, not singletons:

```csharp
// Create kernel for local Docker
var localKernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => { })
    .BuildAsync();

// Create kernel for remote Docker
var remoteKernel = await FluentDockerKernel.Create()
    .WithDockerApi("docker", d => d
        .AtHost("tcp://remote-host:2376")
        .WithCertificates("/certs"))
    .BuildAsync();

// Both kernels operate independently
```

**Benefits:**
- Multiple Docker hosts simultaneously
- Different driver configurations per kernel
- Better testing (isolated kernels)
- Explicit lifecycle management
- No global state

---

## SysCtl() Driver Access

Access drivers via `kernel.SysCtl()` pattern (inspired by Unix sysctl):

```csharp
// Type-safe access
var containerDriver = kernel.SysCtl<IContainerDriver>("docker");
var containers = await containerDriver.ListAsync(new DriverContext());

// Type-based access (runtime resolution)
object driver = kernel.SysCtl("docker", typeof(IContainerDriver));

// Non-throwing — returns false if interface not supported
if (kernel.TrySysCtl<IPodmanPodDriver>("podman", out var podDriver))
{
    await podDriver.CreatePodAsync(context, "my-pod");
}

// Component-based access (legacy, delegates to type-based internally)
var networkDriver = kernel.SysCtl("docker", DriverComponent.Network);
```

> **Note:** `DriverComponent` is `[Obsolete]` and slated for removal in v4 — prefer `SysCtl<T>(driverId)` (or the type-based `SysCtl(driverId, typeof(...))` overload) for new code.

The kernel resolves interfaces through `IDriverInterfaceResolver` when the driver pack or driver implements it, falling back to direct `ISysCtl` delegation and then direct cast. This means any driver can expose custom interfaces without kernel changes. See [Driver Extensibility](extensibility.html) for details.

### Available Driver Components

| Component | Interface | Purpose |
|-----------|-----------|---------|
| Container | `IContainerDriver` | Container lifecycle, exec, logs |
| Image | `IImageDriver` | Pull, build, push, remove images |
| Network | `INetworkDriver` | Create, connect, disconnect networks |
| Volume | `IVolumeDriver` | Create, mount, remove volumes |
| Compose | `IComposeDriver` | Docker Compose operations |
| System | `ISystemDriver` | Info, version, ping, disk usage, prune |
| Pod | `IPodmanPodDriver` | Podman pod operations (Podman-only) |
| Kubernetes | *(Podman-only)* | Kubernetes YAML play, down, generate |
| Machine | *(Podman-only)* | Podman machine init, start, stop |
| Manifest | *(Podman-only)* | Multi-arch manifest create, add, push |

> **Note:** `Auth` (`IAuthDriver`) and `Stream` (`IStreamDriver`) are **not** members of the
> `DriverComponent` enum. They are resolved directly via `IDriverInterfaceResolver` on the
> driver pack (e.g., `kernel.SysCtl<IStreamDriver>("docker")`).

---

## Scoped Builder Pattern

The Builder uses `WithinDriver()` to establish the active scope:

```csharp
// Single driver scope
var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx")
        .WithName("web"))
    .BuildAsync();

// Multi-scope deployment
var deployment = await new Builder()
    .WithinDriver("docker-local", kernel)
    .UseContainer(c => c.UseImage("nginx"))
    .WithinDriver("docker-remote")  // Reuses kernel
    .UseContainer(c => c.UseImage("postgres"))
    .BuildAsync();

// Access results by driver
var localServices = deployment.ForDriver("docker-local");
var remoteServices = deployment.ForDriver("docker-remote");
```

### BuildResults

```csharp
public class BuildResults : IAsyncDisposable, IDisposable
{
    // All services across all scopes
    IReadOnlyList<IServiceAsync> All { get; }

    // Filter services by driver ID
    IReadOnlyList<IServiceAsync> ForDriver(string driverId);

    // Build scopes (one per WithinDriver call)
    IReadOnlyList<BuildScope> Scopes { get; }

    // Typed accessors
    IReadOnlyList<IContainerService> Containers { get; }
    IReadOnlyList<INetworkService> Networks { get; }
    IReadOnlyList<IVolumeService> Volumes { get; }
    IReadOnlyList<IComposeService> ComposeServices { get; }

    // Lookup by name (returns null if not found)
    IContainerService GetContainer(string name);
    INetworkService GetNetwork(string name);
    IVolumeService GetVolume(string name);

    // Filter by service type
    IReadOnlyList<T> OfType<T>() where T : IServiceAsync;

    // Disposal
    ValueTask DisposeAsync();
    Task DisposeAllAsync();
    void Dispose();
    void DisposeAll();
}
```

---

## Driver-Aware Builder Extensions

All builders implement `IDriverScopedBuilder`, providing access to the kernel and driver ID inside builder lambdas. This enables driver-specific fluent extensions that gracefully no-op when the current driver doesn't support the feature:

```csharp
// Podman-specific .UsePod() — no-op on Docker
await new Builder()
    .WithinDriver("podman", kernel)
    .UseContainer(c => c
        .UseImage("redis:7-alpine")
        .UsePod("cache-pod")         // Only applies on Podman
        .ExposePort(6379, 6379))
    .BuildAsync();
```

For full documentation on writing custom driver interfaces, builder extensions, and multi-driver deployment patterns, see [Driver Extensibility](extensibility.html).

---

## Driver Interfaces

### IDriverPack Interface

A driver pack groups related driver implementations under a single registered
entity. Component drivers are resolved at runtime via `ISysCtl`, not through
direct properties.

```csharp
public interface IDriverPack : ISysCtl
{
    DriverType Type { get; }        // values: DockerCli, DockerApi, PodmanCli, PodmanApi, Custom
                                    // (PodmanApi and Custom are reserved for future use)
    RuntimeType Runtime { get; }    // values: Docker, Podman, Containerd, CriO, Unknown
                                    // (Containerd and CriO are reserved for future use)

    Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
    Task InitializeAsync(DriverContext context, CancellationToken ct = default);
}
```

Component drivers (IContainerDriver, IImageDriver, INetworkDriver,
IVolumeDriver, IComposeDriver, ISystemDriver, IAuthDriver, IStreamDriver)
are resolved via `ISysCtl.SysCtl<T>()`:

```csharp
var containers = kernel.SysCtl<IContainerDriver>("docker-cli");
var stream     = kernel.SysCtl<IStreamDriver>("docker-cli");
var system     = kernel.SysCtl<ISystemDriver>("docker-cli");
```

### IContainerDriver Interface (Simplified)

The following shows the core methods. The full interface also includes `RunAsync`, `RestartAsync`, `PauseAsync`, `UnpauseAsync`, `KillAsync`, `RemoveAsync`, and more.

```csharp
public interface IContainerDriver
{
    Task<CommandResponse<ContainerCreateResult>> CreateAsync(
        DriverContext context,
        ContainerCreateConfig config,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<Unit>> StartAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<Unit>> StopAsync(
        DriverContext context,
        string containerId,
        int? timeout = null,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<Container>> InspectAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<IList<Container>>> ListAsync(
        DriverContext context,
        ContainerListFilter filter = null,
        CancellationToken cancellationToken = default);
}
```

---

## Capabilities System

### Capability Discovery

FluentDocker provides granular capability detection with 100+ feature flags:

```csharp
var driverPack = kernel.GetDriverPack("docker");
var caps = await driverPack.GetCapabilitiesAsync();

// Container capabilities
if (caps.Container.SupportsHealthChecks) { /* ... */ }
if (caps.Container.SupportsResourceLimits) { /* ... */ }

// Image capabilities
if (caps.Image.SupportsBuildx) { /* ... */ }
if (caps.Image.SupportsMultiPlatform) { /* ... */ }

// Docker-specific
if (caps.DockerSpecific.SupportsSwarm) { /* ... */ }
if (caps.DockerSpecific.SupportsContentTrust) { /* ... */ }
```

### Interface Discovery

Driver packs that implement `IDriverInterfaceResolver` allow runtime discovery of supported interfaces:

```csharp
// Check supported interfaces via IDriverInterfaceResolver
var resolver = driverPack as IDriverInterfaceResolver;
IReadOnlyCollection<Type> supported = resolver.GetSupportedInterfaces();

// Try to resolve a specific interface
if (resolver.TryResolve(typeof(IStreamDriver), out var impl))
{
    var stream = (IStreamDriver)impl;
}
```

The driver interfaces (`IContainerDriver`, `IImageDriver`, `INetworkDriver`, `IVolumeDriver`, `IComposeDriver`, `ISystemDriver`) provide the full API surface. Each driver pack exposes the interfaces it supports, discoverable via `GetSupportedInterfaces()`.

---

## Multi-Environment Example

```csharp
public async Task DeployAsync(CancellationToken cancellationToken)
{
    var kernel = await FluentDockerKernel.Create()
        .WithDockerCli("dev", d => { })
        .WithDockerApi("prod", d => d
            .AtHost("tcp://prod:2376")
            .WithCertificates("/certs"))
        .BuildAsync(cancellationToken);

    var deployment = await new Builder()
        .WithinDriver("dev", kernel)
        .UseContainer(c => c.UseImage("myapp:dev"))
        .UseContainer(c => c.UseImage("postgres:14"))
        .WithinDriver("prod")  // Reuses kernel
        .UseContainer(c => c.UseImage("myapp:v1.0"))
        .BuildAsync(cancellationToken);

    // Start all services in parallel
    var startTasks = deployment.All
        .OfType<IContainerService>()
        .Select(c => c.StartAsync(cancellationToken));

    await Task.WhenAll(startTasks);

    // Cleanup
    await deployment.DisposeAllAsync();
}
```

---

## Progress Reporting

Long-running operations support `IProgress<T>`:

```csharp
var progress = new Progress<ImagePullProgress>(p =>
{
    Console.WriteLine($"Pulling: {p.Status} - {p.Progress}");
});

var driver = kernel.SysCtl<IImageDriver>("docker");
await driver.PullAsync(
    context,
    "nginx",
    "latest",
    progress,
    CancellationToken.None);
```

---

## Key Architecture Decisions

### 1. No Singleton Kernel
**Rationale:** Multiple Docker hosts, better testing, explicit lifecycle, no global state.

### 2. SysCtl() Interface
**Rationale:** Clean, discoverable API; type-safe with generics; Unix-inspired; consistent access pattern.

### 3. Driver Registration with IDs
**Rationale:** Multiple instances of same driver type; different configurations; flexible naming.

### 4. Terminal BuildAsync()
**Rationale:** Clear execution point; modern async/await; simpler API; consistent pattern.

### 5. Services Reference Kernel
**Rationale:** Access to full kernel capabilities; can switch drivers dynamically; cleaner API.

---

## Error Handling

FluentDocker uses a simple exception hierarchy:

- `FluentDockerException` -- base exception (message + innerException)
- `FluentDockerNotSupportedException` -- operation not supported by driver
- `DriverException` -- driver-level failure with `ErrorCode`, `Context` (ErrorContext), and `IsTransient` properties
  - `DriverNotFoundException` -- driver ID not registered
  - `DriverNotAvailableException` -- driver not healthy/reachable
- `ContainerNotFoundException`, `ContainerStartException`
- `ImageNotFoundException`, `ImagePullException`
- `CapabilityNotSupportedException`, `InterfaceNotSupportedException`
- `PodmanMachineNotRunningException`

Error codes use a category-prefixed format defined in `ErrorCodes`:

| Category | Format | Examples |
|----------|--------|---------|
| General | `GEN_NNN` | `GEN_000` (Unknown), `GEN_003` (Timeout) |
| Driver | `DRV_NNN` | `DRV_001` (NotFound), `DRV_006` (InterfaceNotSupported) |
| Container | `CNT_NNN` | `CNT_001` (NotFound), `CNT_006` (CreateFailed) |
| Image | `IMG_NNN` | `IMG_001` (NotFound), `IMG_002` (PullFailed) |
| Network | `NET_NNN` | `NET_001` (NotFound), `NET_003` (CreateFailed) |
| Volume | `VOL_NNN` | `VOL_001` (NotFound), `VOL_003` (CreateFailed) |
| Compose | `CMP_NNN` | `CMP_001` (FileNotFound), `CMP_003` (UpFailed) |
| Pod | `POD_NNN` | `POD_001` (NotFound), `POD_002` (CreateFailed) |
| API | `API_NNN` | `API_404` (NotFound), `API_CONN` (ConnectionFailed) |

`ErrorContext` provides diagnostic details: `OperationId`, `DriverId`, `Host`, `Operation`, `ExitCode`, `StdOut`, `StdErr`, `Metadata`, and `Timestamp`.

---

## Summary

FluentDocker v3.0 provides:

- **Multiple runtimes**: Docker, Podman, future runtimes
- **Multiple instances**: Same driver type, different configurations
- **Multiple kernels**: Isolated instances, no global state
- **Clean driver access**: SysCtl() interface pattern with `TrySysCtl<T>()` for feature checks
- **Driver extensibility**: Custom interfaces via `IDriverInterfaceResolver` ([details](extensibility.html))
- **Driver-aware builders**: `IDriverScopedBuilder` with `RequireDriver<T>()` / `TryDriver<T>()`
- **Better testing**: Mock drivers, isolated kernels
- **Multi-host support**: Multiple Docker hosts simultaneously
- **Full async**: All operations with CancellationToken support
- **Capability discovery**: 100+ feature flags for runtime adaptation
