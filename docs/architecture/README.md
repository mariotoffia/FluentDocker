# FluentDocker v3.0.0 - Driver Layer Architecture Documentation

## Overview

This directory contains comprehensive architecture, implementation, and migration documentation for FluentDocker v3.0.0's driver layer refactoring.

---

## Documents

### 0. **TERMINAL_BUILD_PATTERN.md** 🔥 **READ THIS FIRST - BREAKING CHANGE**

**Terminal BuildAsync() pattern - fundamental API redesign in v3.0.0**

**Critical Changes:**
1. `BuildAsync()` is now TERMINAL in all fluent APIs - returns `Task<TResult>`
2. All operations are asynchronous (async/await throughout)

**Quick Examples:**
```csharp
// Kernel - BuildAsync() returns Task<FluentDockerKernel>
var kernel = await FluentDockerKernel.Create()
    .WithDriver("docker", d => d.UseDockerCli())
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

**What Changed:**
- Lambda configuration everywhere (cleaner syntax)
- Single BuildAsync() call at the end (async/await pattern)
- BuildAsync() executes all operations and returns results
- All driver operations return `Task<CommandResponse<T>>`
- All service operations are async (`StartAsync()`, `StopAsync()`, etc.)
- .NET 10.0.100 single-framework targeting

**Read this first** to understand the breaking changes before other docs.

---

### 0.1. **ASYNC_OPERATIONS.md** ⚡ **ASYNC/AWAIT PATTERN**

**Comprehensive async/await implementation - all operations are asynchronous**

**Core Principles:**
- `BuildAsync()` is terminal and returns `Task<TResult>`
- All driver operations return `Task<CommandResponse<T>>`
- All service operations are async (`StartAsync()`, `StopAsync()`, etc.)
- `CancellationToken` support throughout
- `IAsyncDisposable` implementation

**Covers:**
- Async builder pattern with `BuildAsync()`
- Async driver interfaces (Container, Image, Network, Volume, Compose, System)
- Async service operations with cancellation
- Progress reporting with `IProgress<T>`
- Parallel execution patterns
- Async disposal (`DisposeAllAsync()`)
- Complete migration guide from sync to async

**Example:**
```csharp
var kernel = await FluentDockerKernel.Create()
    .WithDriver("docker", d => d.UseDockerCli())
    .BuildAsync();  // ASYNC

var deployment = await new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer(c => c.UseImage("nginx"))
    .BuildAsync(cancellationToken);  // ASYNC with cancellation

await deployment.All[0].StartAsync();
await deployment.DisposeAllAsync();
```

**Read this** for complete async implementation details and patterns.

---

### 1. **DRIVER_LAYER_ARCHITECTURE_V3.md** ⭐ **CORE ARCHITECTURE**

**The main architecture document** describing the v3.0.0 design with breaking changes.

**Key Concepts:**
- **Non-singleton kernel**: `FluentDockerKernel` is instantiable, multiple instances supported
- **Terminal Build()**: Build() executes all operations and returns final result (no nested builds)
- **Lambda configuration**: All builders use lambda syntax for clean configuration
- **Driver registration**: `WithDriver(id, d => d.UseDockerCli())` with lambda configuration
- **SysCtl() interface**: `kernel.SysCtl<IContainerDriver>("docker-id")`
- **Multiple driver instances**: Same driver type, different IDs and configurations
- **Scoped fluent API**: `new Builder().WithinDriver(driverId, kernel)`
- **Kernel reuse**: If kernel omitted in `WithinDriver()`, last kernel is reused
- **Multi-scope deployments**: Track resources across multiple drivers/kernels
- **Services reference kernel**: `container.Kernel`, `container.DriverId`, `container.Context`

**Contents:**
- Current architecture analysis (71 ProcessExecutor usages, 100% CLI-based)
- Proposed v3.0.0 architecture overview
- Core components (Kernel, Registry, SysCtl, Drivers)
- Usage examples (single host, multiple hosts, Docker + Podman)
- Driver implementations (Docker CLI, Docker API, Podman CLI)
- Multi-driver support patterns
- Backward compatibility strategy (breaking changes acceptable)

**Read this first** to understand the overall design philosophy and architecture.

---

### 2. **IMPLEMENTATION_PLAN_V3.md**

**Detailed 20-day implementation plan** with code examples and checklists.

**Timeline:**
- **Phase 1 (Days 1-3)**: Core infrastructure
  - Enums: DriverType, RuntimeType, DriverComponent, PreferredDriverType
  - Exceptions: DriverNotFoundException, DriverException
  - Models: DriverContext, DriverPreferences, DriverCapabilities, DriverHealthStatus
  - Interfaces: IDriver, IContainerDriver, IImageDriver, INetworkDriver, IVolumeDriver, IComposeDriver, ISystemDriver

- **Phase 2 (Days 4-6)**: Kernel infrastructure
  - IDriverRegistry with ID-based registration
  - DriverRegistry implementation
  - IDriverSelector and DefaultDriverSelector
  - FluentDockerKernel (non-singleton)
  - SysCtl() method implementations
  - FluentDocker static helper (default kernel)

- **Phase 3 (Days 7-9)**: Update Builders
  - Builder with `WithinDriver()` scoping pattern
  - BuildScope and BuildResults classes for multi-scope tracking
  - ContainerBuilder with continuation (Build() returns Builder)
  - BuildAndGet() method for direct service retrieval
  - Kernel reuse when omitted in WithinDriver()

- **Phase 4 (Days 10-12)**: Update Services
  - IContainerService interface changes
  - DockerContainerService uses kernel.SysCtl()
  - All services updated (Host, Network, Volume, Compose, Image)
  - Services use DriverContext instead of DockerUri + ICertificatePaths

- **Phase 5 (Days 13-17)**: Docker CLI Driver
  - DockerCliDriver implementation
  - DockerCliContainerDriver (migrate from Commands/Client.cs)
  - DockerCliImageDriver (migrate from Commands/Images.cs)
  - DockerCliNetworkDriver (migrate from Commands/Network.cs)
  - DockerCliVolumeDriver (migrate from Commands/Volumes.cs)
  - DockerComposeCliDriver (migrate from Commands/Compose.cs)
  - DockerCliSystemDriver (migrate from Commands/Info.cs)

- **Phase 6 (Days 18-20)**: Testing
  - Multiple kernel instance tests
  - Multiple driver instance tests
  - SysCtl() interface tests
  - Driver selection tests
  - Integration tests

**Contents:**
- New project structure (Drivers/, Kernel/ folders)
- Complete code examples for every component
- Step-by-step tasks with file paths
- Implementation checklist for each phase
- Total duration: 20 working days (~4 weeks)

**Use this** as your implementation guide with concrete steps and code.

---

### 3. **MIGRATION_GUIDE_V3.md**

**User migration guide** from v2.x.x to v3.0.0.

**Breaking Changes:**
- Builder uses WithinDriver() scoping pattern (no kernel in constructor)
- Build() returns Builder for continuation; use BuildAndGet() for service
- Services API changes (Kernel/DriverId/Context properties)
- Host configuration via driver registration
- DockerHost/Certificates moved to Context

**Migration Effort:**
| Use Case | Effort | Changes Required |
|----------|--------|------------------|
| Simple local Docker | **None** | Use default kernel (no changes) |
| Docker Compose | **None** | Use default kernel (no changes) |
| Remote Docker host | **Low** | Register driver with host/certs |
| Multiple hosts | **Medium** | Register multiple drivers |
| Custom testing | **Low-Medium** | Use mock drivers |

**Contents:**
- Quick start (minimal changes)
- Breaking changes detailed explanation
- Before/after code comparisons
- Migration scenarios for common use cases
- New features showcase
- API changes reference table
- Troubleshooting guide
- Performance considerations

**Use this** when migrating existing v2.x.x code to v3.0.0.

---

### 4. **ERROR_HANDLING_STRATEGY_V3.md**

**Complete error handling and exception strategy** for v3.0.0.

**Current v2.x.x Issues:**
- Only 2 exception types (FluentDockerException, FluentDockerNotSupportedException)
- No error codes - string parsing required
- No diagnostic context
- Minimal logging (debug traces only)
- No retry mechanisms

**v3.0.0 Improvements:**
- **20+ typed exceptions**: ContainerNotFoundException, ImagePullException, DriverNotAvailableException, etc.
- **Error codes**: Hierarchical codes (e.g., `ErrorCodes.Container.NotFound`)
- **Error context**: OperationId, DriverId, Host, Operation, ExitCode, StdOut/StdErr
- **IsTransient flag**: Automatic transient error detection
- **Enhanced CommandResponse**: Includes ErrorContext and ErrorCode
- **Retry mechanisms**: RetryPolicy and RetryExecutor
- **Structured logging**: IFluentDockerLogger with log levels
- **Metrics/telemetry**: IFluentDockerMetrics for observability

**Contents:**
- Current error handling analysis
- Complete exception hierarchy (20+ types)
- Error codes system (50+ codes)
- ErrorContext class for diagnostics
- Enhanced CommandResponse<T>
- Error handling patterns per layer (Driver, Service, Builder, Kernel)
- Retry and recovery mechanisms
- Logging and observability framework
- Best practices for library users and driver implementers

**Use this** to understand the complete error handling design.

---

### 5. **ERROR_HANDLING_MIGRATION_V3.md**

**Step-by-step migration guide** for error handling changes.

**Exception Type Mapping:**
- v2.x.x `FluentDockerException` → v3.0.0 specific types
- Comprehensive mapping table

**6 Detailed Migration Scenarios:**
1. Basic container operations (minimal vs specific exception handling)
2. Image pull operations (with retry support)
3. Container start/stop (state tracking)
4. Docker Compose (file not found, validation, runtime errors)
5. Driver management (registration, selection, health checks)
6. Validation errors (detailed error lists)

**Advanced Topics:**
- Error code usage for programmatic handling
- Using ErrorContext for diagnostics
- Retry patterns (built-in, custom, manual)
- Logging integration (custom adapters)
- Testing error handling

**Migration Checklist:**
- For library maintainers (9 phases, 6 weeks)
- For library users (by user type)

**Contents:**
- Quick migration reference
- Before/after code examples for 6 scenarios
- Error code usage patterns
- Error context extraction
- Retry pattern implementations
- Logging integration examples
- Testing strategies
- Complete migration checklists

**Use this** when migrating error handling from v2.x.x to v3.0.0.

---

### 6. **FLUENT_API_AND_CAPABILITIES_V3.md**

**Fluent API for driver registration and composable interfaces** for v3.0.0.

**Fluent Kernel Configuration:**
- Fluent builder pattern for kernel creation
- Driver-specific builders (DockerCli, DockerApi, PodmanCli)
- Intuitive method chaining for registration
- Configuration methods: AtHost(), WithCertificates(), AsPriority(), AsDefault()

**Composable Interface System (30+ sub-interfaces):**
- **IContainerDriver** → 8 focused interfaces (Lifecycle, Inspection, Execution, Files, Logs, Stats, Processes, Health)
- **IImageDriver** → 6 focused interfaces (Lifecycle, Build, BuildAdvanced, Registry, Inspection, Export)
- **INetworkDriver** → 3 focused interfaces (Lifecycle, Connectivity, Inspection)
- **IVolumeDriver** → 2 focused interfaces (Lifecycle, Inspection)
- **IComposeDriver** → 3 focused interfaces (Lifecycle, Operations, Inspection)
- **ISystemDriver** → 4 focused interfaces (Info, Auth, Events, Maintenance)
- **Podman-specific** → 3 interfaces (PodDriver, KubernetesYaml, SystemdGeneration)

**Enhanced Capability System (100+ flags):**
- DriverCapabilities with granular feature detection
- ContainerCapabilities, ImageCapabilities, NetworkCapabilities, VolumeCapabilities
- DockerSpecificCapabilities (Swarm, secrets, BuildX, content trust)
- PodmanSpecificCapabilities (pods, Kubernetes YAML, systemd, rootless)
- SecurityCapabilities (Docker: 14 capabilities, Podman: 11 capabilities)
- PerformanceCapabilities (streaming, bulk operations, async)
- Type-safe capability checking with Implements<T>()

**Fd.XXX Static Method Removal:**
- Migration from Fd.DisposeOnException to using statements
- Extension method alternatives provided
- Complete migration examples

**Research-Based Features:**
- Docker 2024-2025 capabilities (Swarm, BuildX, content trust, BuildCloud)
- Podman 2024-2025 capabilities (pods, Kubernetes YAML, systemd, rootless by default)
- Performance characteristics and security differences

**Contents:**
- Fluent kernel configuration API with examples
- Complete composable interface breakdown
- Capability system with 100+ flags
- Driver-specific capability implementations
- Fd.XXX removal migration guide
- New exceptions: InterfaceNotSupportedException, CapabilityNotSupportedException

**Use this** for fluent driver registration, understanding composable interfaces, and capability discovery.

---

## V3 Architecture Documents

All documents in this directory define the v3.0.0 architecture with breaking changes:

**Core Architecture:**
- **TERMINAL_BUILD_PATTERN.md** - Read first: `BuildAsync()` is terminal
- **ASYNC_OPERATIONS.md** - Comprehensive async patterns
- **DRIVER_LAYER_ARCHITECTURE_V3.md** - Main architecture document
- **IMPLEMENTATION_PLAN_V3.md** - Implementation roadmap
- **MIGRATION_GUIDE_V3.md** - User migration guide

**Error Handling:**
- **ERROR_HANDLING_STRATEGY_V3.md** - Exception hierarchy and patterns
- **ERROR_HANDLING_MIGRATION_V3.md** - Error handling migration guide

**Fluent API & Capabilities:**
- **FLUENT_API_AND_CAPABILITIES_V3.md** - Driver registration and capability system

**Testing:**
- **TEST_PLAN_V3.md** - Test plan and verification checklist

---

## Key Architecture Decisions

### 1. No Singleton Kernel

**Decision**: FluentDockerKernel is instantiable, not a singleton.

**Rationale**:
- Support multiple kernel instances
- Better testing (isolated kernels)
- Explicit lifecycle management
- No global state pollution
- Multiple Docker hosts simultaneously

**Impact**: Breaking change - users must pass kernel to Builder or use default.

### 2. SysCtl() Interface

**Decision**: Access drivers via `kernel.SysCtl("id", component)` pattern.

**Rationale**:
- Clean, discoverable API
- Type-safe with generics
- Unix-inspired (sysctl)
- Consistent access pattern
- Supports component-level access

**Impact**: New capability - easier direct driver access.

### 3. Driver Registration with IDs

**Decision**: Register drivers with unique string IDs.

**Rationale**:
- Multiple instances of same driver type
- Multiple Docker hosts with different IDs
- Clear driver identification
- Flexible naming (user-defined IDs)
- Enables multi-host scenarios

**Impact**: Breaking change - driver registration API different from v2.

### 4. Fluent API Kernel Binding

**Decision**: Builder accepts kernel in constructor.

**Rationale**:
- Explicit kernel association
- No hidden global state
- Better testability
- Clear ownership
- Supports multiple kernels

**Impact**: Breaking change - `new Builder()` optionally takes kernel (uses default if omitted).

### 5. Services Reference Kernel

**Decision**: Services store kernel reference, not DockerUri.

**Rationale**:
- Access to full kernel capabilities
- Can switch drivers dynamically
- SysCtl() access from services
- Cleaner API (one reference instead of host + certs)
- Future-proof

**Impact**: Breaking change - Service interfaces modified.

### 6. Typed Exception Hierarchy

**Decision**: 20+ typed exceptions with error codes, context, and transient flags.

**Rationale**:
- Specific exception types for precise error handling
- Error codes for programmatic decisions
- ErrorContext for diagnostics (OperationId, DriverId, etc.)
- IsTransient flag for retry decisions
- Enhanced CommandResponse with error information
- Better observability with structured logging

**Impact**: Breaking change - new exception types, but backward compatible (can still catch FluentDockerException).

---

## Quick Reference

### Creating a Kernel (Async)

```csharp
// Fluent kernel builder with async
var kernel = await FluentDockerKernel.Create()
    .WithDriver("docker", d => d.UseDockerCli())
    .BuildAsync();

// Manual registration (still sync)
var kernel = new FluentDockerKernel(new FluentDockerKernelOptions
{
    AutoRegisterDrivers = false
});
await kernel.RegisterDriverAsync("docker", new DockerCliDriver());

// Use default kernel
var kernel = FluentDocker.DefaultKernel;
```

### Registering Drivers (Async)

```csharp
// Fluent registration with lambda configuration
var kernel = await FluentDockerKernel.Create()
    .WithDriver("docker-local", d => d.UseDockerCli())
    .WithDriver("docker-prod", d => d
        .UseDockerApi()
        .AtHost("tcp://prod:2376")
        .WithCertificates("/certs"))
    .WithDriver("podman", d => d.UsePodmanCli())
    .BuildAsync();
```

### Using Fluent API with WithinDriver() (Async)

```csharp
// Single scope with async
var deployment = await new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer(c => c.UseImage("nginx"))
    .BuildAsync();  // TERMINAL ASYNC

var container = deployment.All[0] as IContainerService;
await container.StartAsync();

// Multi-scope deployment with async
var deployment = await new Builder()
    .WithinDriver("docker-prod", prodKernel)
        .UseContainer(c => c.UseImage("myapp:v1.0"))
    .WithinDriver("docker-staging", stagingKernel)
        .UseContainer(c => c.UseImage("myapp:v1.0"))
    .BuildAsync();  // TERMINAL ASYNC - returns all services

// deployment.ForDriver("docker-prod") => [container]
// deployment.ForDriver("docker-staging") => [container]

// Kernel reuse with async
var results = await new Builder()
    .WithinDriver("docker-1", kernel)  // Set kernel
        .UseContainer(c => c.UseImage("nginx"))
    .WithinDriver("docker-2")  // Reuses kernel
        .UseContainer(c => c.UseImage("postgres"))
    .BuildAsync();  // TERMINAL ASYNC
```

### Using SysCtl() (Async)

```csharp
// Type-safe access with async driver calls
var containerDriver = kernel.SysCtl<IContainerDriver>("docker");
var containers = await containerDriver.ListAsync(
    new DriverContext(),
    cancellationToken: ct);

// Component-based access
var networkDriver = kernel.SysCtl("docker", DriverComponent.Network);
var networks = await networkDriver.ListAsync(new DriverContext());

// Get entire driver
var driver = kernel.GetDriver("docker");
```

---

## Next Steps

### For Architecture Review

1. Read **DRIVER_LAYER_ARCHITECTURE_V3.md**
2. Review design decisions
3. Validate use cases match requirements
4. Approve or provide feedback

### For Implementation

1. Follow **IMPLEMENTATION_PLAN_V3.md**
2. Start with Phase 1 (Core Infrastructure)
3. Use provided code examples
4. Check off implementation checklist
5. Run tests at each phase

### For Migration

1. Read **MIGRATION_GUIDE_V3.md**
2. Identify which scenario matches your code
3. Apply minimal changes first (use default kernel)
4. Adopt new features incrementally
5. Test thoroughly

---

## Summary

FluentDocker v3.0.0 introduces a **pluggable driver architecture** with **comprehensive error handling** that:

**Driver Architecture:**
✅ **Supports multiple runtimes**: Docker, Podman, future runtimes
✅ **Multiple instances**: Same driver type, different configurations
✅ **Multiple kernels**: Isolated instances, no global state
✅ **Clean driver access**: SysCtl() interface pattern
✅ **Better testing**: Mock drivers, isolated kernels
✅ **Multi-host support**: Multiple Docker hosts simultaneously
✅ **Flexibility**: Driver plugins, custom implementations

**Error Handling:**
✅ **Typed exceptions**: 20+ specific exception types (ContainerNotFoundException, ImagePullException, etc.)
✅ **Error codes**: 50+ hierarchical codes for programmatic handling
✅ **Error context**: Rich diagnostics (OperationId, DriverId, Host, ExitCode, StdOut/StdErr)
✅ **Transient detection**: IsTransient flag for automatic retry decisions
✅ **Retry mechanisms**: Built-in RetryPolicy and RetryExecutor
✅ **Structured logging**: IFluentDockerLogger with log levels
✅ **Metrics/telemetry**: IFluentDockerMetrics for observability
✅ **Enhanced CommandResponse**: Includes error context and codes

**Fluent API & Capabilities:**
✅ **Fluent configuration**: Intuitive builder pattern for kernel and driver setup
✅ **Composable interfaces**: 30+ focused sub-interfaces for fine-grained implementation
✅ **Capability discovery**: 100+ flags for feature detection and runtime adaptation
✅ **Docker/Podman features**: Research-based capability system (Swarm, pods, BuildX, Kubernetes YAML)
✅ **Type-safe checking**: Implements<T>() for interface and capability validation
✅ **Fd.XXX removal**: Migration to modern patterns (using statements, extension methods)

**Breaking Changes Acceptable:**
✅ v3.0.0 allows architectural improvements for long-term benefits

The architecture is **cleaner, more testable, more observable, and more flexible** than v2.x.x while maintaining FluentDocker's elegant fluent API.

---

## Questions?

If you have questions about the architecture or implementation:

1. Check the relevant document (Architecture, Implementation, or Migration)
2. Review the Quick Reference above
3. Look at code examples in each document
4. Refer to use case scenarios

All documents include extensive examples and detailed explanations.
