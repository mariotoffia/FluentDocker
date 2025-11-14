# FluentDocker v3.0.0 - Driver Layer Architecture Documentation

## Overview

This directory contains comprehensive architecture, implementation, and migration documentation for FluentDocker v3.0.0's driver layer refactoring.

---

## Documents

### 1. **DRIVER_LAYER_ARCHITECTURE_V3.md** ⭐ **START HERE**

**The main architecture document** describing the v3.0.0 design with breaking changes.

**Key Concepts:**
- **Non-singleton kernel**: `FluentDockerKernel` is instantiable, multiple instances supported
- **Driver registration with IDs**: `kernel.RegisterDriver("docker-local", driver)`
- **SysCtl() interface**: `kernel.SysCtl<IContainerDriver>("docker-id")`
- **Multiple driver instances**: Same driver type, different IDs and configurations
- **Kernel-bound fluent API**: `new Builder(kernel)`
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
  - Builder accepts kernel: `new Builder(kernel)`
  - ContainerBuilder.UseDriver("id") method
  - All builders updated to pass kernel
  - Build() creates services with kernel reference

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
- Builder requires kernel parameter (or uses default)
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

## Document Comparison

### Original Documents (v2.x.x compatible)

These documents were created assuming backward compatibility:

- **DRIVER_LAYER_ARCHITECTURE.md** - Original design with singleton kernel
- **IMPLEMENTATION_PLAN_KERNEL_AND_DRIVERS.md** - Implementation with singleton
- **MIGRATION_PLAN.md** - 100% backward compatible migration

**Status**: These are now superseded by v3 documents but kept for reference.

### V3 Documents (Breaking changes)

These documents embrace v3.0.0 breaking changes for better architecture:

- **DRIVER_LAYER_ARCHITECTURE_V3.md** ⭐
- **IMPLEMENTATION_PLAN_V3.md** ⭐
- **MIGRATION_GUIDE_V3.md** ⭐

**Status**: These are the **official v3.0.0 documents** to follow.

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

---

## Quick Reference

### Creating a Kernel

```csharp
// Auto-register available drivers
var kernel = new FluentDockerKernel();

// Manual registration
var kernel = new FluentDockerKernel(new FluentDockerKernelOptions
{
    AutoRegisterDrivers = false
});
kernel.RegisterDriver("docker", new DockerCliDriver());

// Use default kernel
var kernel = FluentDocker.DefaultKernel;
```

### Registering Drivers

```csharp
// Single local Docker
kernel.RegisterDriver("docker-local", new DockerCliDriver());

// Multiple Docker hosts
kernel.RegisterDriver("docker-dev", new DockerCliDriver(devHost, devCerts));
kernel.RegisterDriver("docker-prod", new DockerApiDriver(prodHost, prodCerts));

// Docker + Podman
kernel.RegisterDriver("docker", new DockerCliDriver());
kernel.RegisterDriver("podman", new PodmanCliDriver());
```

### Using Fluent API

```csharp
// With default kernel (no changes from v2)
var container = new Builder()
    .UseContainer()
    .UseImage("nginx")
    .Build();

// With explicit kernel
var container = new Builder(kernel)
    .UseContainer()
    .UseDriver("docker-prod")  // Specify driver
    .UseImage("nginx")
    .Build();
```

### Using SysCtl()

```csharp
// Type-safe access
var containerDriver = kernel.SysCtl<IContainerDriver>("docker");
var containers = containerDriver.List(new DriverContext());

// Component-based access
var networkDriver = kernel.SysCtl("docker", DriverComponent.Network);
var networks = networkDriver.List(new DriverContext());

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

FluentDocker v3.0.0 introduces a **pluggable driver architecture** that:

✅ **Supports multiple runtimes**: Docker, Podman, future runtimes
✅ **Multiple instances**: Same driver type, different configurations
✅ **Multiple kernels**: Isolated instances, no global state
✅ **Clean driver access**: SysCtl() interface pattern
✅ **Better testing**: Mock drivers, isolated kernels
✅ **Multi-host support**: Multiple Docker hosts simultaneously
✅ **Flexibility**: Driver plugins, custom implementations
✅ **Breaking changes**: v3.0.0 allows architectural improvements

The architecture is **cleaner, more testable, and more flexible** than singleton approach while maintaining FluentDocker's elegant fluent API.

---

## Questions?

If you have questions about the architecture or implementation:

1. Check the relevant document (Architecture, Implementation, or Migration)
2. Review the Quick Reference above
3. Look at code examples in each document
4. Refer to use case scenarios

All documents include extensive examples and detailed explanations.
