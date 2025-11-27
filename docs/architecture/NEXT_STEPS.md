# FluentDocker v3.0.0 - Next Steps

## Current Implementation Status

### Completed
| Component | Status | Location |
|-----------|--------|----------|
| FluentDockerKernel | ✅ Complete | `Kernel/FluentDockerKernel.cs` |
| KernelBuilder | ✅ Complete | `Kernel/KernelBuilder.cs` |
| IDriverRegistry | ✅ Complete | `Kernel/IDriverRegistry.cs` |
| DriverRegistry | ✅ Complete | `Kernel/DriverRegistry.cs` |
| IDriver | ✅ Complete | `Drivers/IDriver.cs` |
| IContainerDriver | ✅ Complete | `Drivers/IContainerDriver.cs` |
| IImageDriver | ✅ Complete | `Drivers/IImageDriver.cs` |
| INetworkDriver | ✅ Complete | `Drivers/INetworkDriver.cs` |
| IVolumeDriver | ✅ Complete | `Drivers/IVolumeDriver.cs` |
| ISystemDriver | ✅ Complete | `Drivers/ISystemDriver.cs` |
| IComposeDriver | ✅ Complete | `Drivers/IComposeDriver.cs` |
| DockerCliDriver | ✅ Complete | `Drivers/Docker/Cli/DockerCliDriver.cs` |
| V3 Builder | ✅ Complete | `Builders/V3/Builder.cs` |
| BuildResults | ✅ Complete | `Model/Kernel/BuildResults.cs` |
| BuildScope | ✅ Complete | `Model/Kernel/BuildScope.cs` |
| ContainerBuilder | ✅ Complete | `Builders/V3/Builder.cs` |
| Error Handling | ✅ Complete | `Common/` and `Model/Drivers/` |

### Partially Complete
| Component | Status | Notes |
|-----------|--------|-------|
| NetworkBuilder | ⚠️ Stub | `ExecuteAsync()` throws `NotImplementedException` |
| VolumeBuilder | ⚠️ Stub | `ExecuteAsync()` throws `NotImplementedException` |
| ContainerServiceAsync | ⚠️ Stub | Needs full implementation |

### Not Started
| Component | Status | Notes |
|-----------|--------|-------|
| UseCompose(lambda) | ❌ | Not in V3 Builder interface |
| Service async methods | ❌ | IService needs StartAsync, StopAsync, etc. |
| Mock Driver | ❌ | For testing without Docker daemon |
| Unit Tests | ❌ | Most test infrastructure exists but tests missing |

---

## Priority 1: Complete Builder Implementations

### 1.1 NetworkBuilder Implementation
**File**: `Builders/V3/Builder.cs`

The `NetworkBuilder.ExecuteAsync()` needs to:
- Use `kernel.SysCtl<INetworkDriver>(driverId)` to get the network driver
- Call `CreateAsync()` on the network driver
- Return a `NetworkService` (or create a new `NetworkServiceAsync`)

### 1.2 VolumeBuilder Implementation
**File**: `Builders/V3/Builder.cs`

The `VolumeBuilder.ExecuteAsync()` needs to:
- Use `kernel.SysCtl<IVolumeDriver>(driverId)` to get the volume driver
- Call `CreateAsync()` on the volume driver
- Return a `VolumeService` (or create a new `VolumeServiceAsync`)

### 1.3 Add UseCompose to Builder
**File**: `Builders/V3/Builder.cs`

Add:
```csharp
public Builder UseCompose(Action<IComposeBuilder> configure)
{
    ValidateScope();
    var builder = new ComposeBuilder(_currentKernel, _currentDriverId);
    configure(builder);
    _operations.Add(new BuildOperation
    {
        Kernel = _currentKernel,
        DriverId = _currentDriverId,
        ExecuteAsync = ct => builder.ExecuteAsync(ct)
    });
    return this;
}
```

---

## Priority 2: Service Async Updates

### 2.1 Update IService Interface
**File**: `Services/IService.cs`

Add async methods:
```csharp
public interface IService : IDisposable, IAsyncDisposable
{
    // Existing sync methods...
    
    // New async methods
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default);
    
    // Async disposal
    ValueTask DisposeAsync();
}
```

### 2.2 Update Service Implementations
All service implementations need to:
1. Reference the kernel instead of DockerUri
2. Use `kernel.SysCtl<T>(driverId)` for driver access
3. Implement async methods
4. Implement `IAsyncDisposable`

---

## Priority 3: Testing

### 3.1 Unit Tests (No Docker Required)

Create tests for:
1. `DriverRegistryTests` - Registration, retrieval, default driver
2. `FluentDockerKernelTests` - Kernel creation, SysCtl access
3. `KernelBuilderTests` - Fluent API, BuildAsync
4. `BuilderTests` - WithinDriver, UseContainer, BuildAsync
5. `ExceptionTests` - All exception types

### 3.2 Mock Driver Implementation

Create `MockDriver` class that:
- Implements all driver interfaces
- Returns configurable responses
- Tracks method calls for verification
- Supports success/failure scenarios

### 3.3 Integration Tests (Requires Docker)

Write integration tests for:
- Container lifecycle (create, start, stop, remove)
- Image operations (pull, list, remove)
- Network operations (create, connect, disconnect, remove)
- Volume operations (create, list, remove)
- Compose operations (up, down)

---

## Priority 4: Documentation Updates

### 4.1 Update Examples
Update all code examples in documentation to use the async patterns:
- `await kernel.BuildAsync()` instead of `kernel.Build()`
- `await builder.BuildAsync()` instead of `builder.Build()`
- `await service.StartAsync()` instead of `service.Start()`

### 4.2 API Reference
Generate XML documentation for all public APIs.

---

## Implementation Order

| Week | Focus Area | Deliverables |
|------|------------|--------------|
| 1 | Builder completion | NetworkBuilder, VolumeBuilder, UseCompose |
| 1 | Service stubs | ContainerServiceAsync completion |
| 2 | Service async | IService updates, service implementations |
| 2 | Unit tests | Core unit tests (no Docker required) |
| 3 | Mock driver | Complete mock driver implementation |
| 3 | Integration tests | Basic integration test suite |
| 4 | Documentation | Example updates, API reference |

---

## Quick Start for Developers

### Running the V3 API Today

```csharp
// Create kernel with Docker CLI driver
var kernel = await FluentDockerKernel.Create()
    .WithDriver("docker", d => d.UseDockerCli())
    .BuildAsync();

// Create a container
var results = await new Ductus.FluentDocker.Builders.V3.Builder()
    .WithinDriver("docker", kernel)
        .UseContainer(c => c
            .UseImage("nginx:alpine")
            .WithName("test-container"))
    .BuildAsync();

// Access the created service
var container = results.All[0];

// Cleanup
await results.DisposeAllAsync();
kernel.Dispose();
```

### What Works Now
- ✅ Kernel creation and driver registration
- ✅ Container creation via V3 Builder
- ✅ All Docker CLI operations (via DockerCliDriver)
- ✅ BuildResults with scope tracking
- ✅ Async disposal

### What Needs Work
- ⚠️ NetworkBuilder and VolumeBuilder (throw NotImplementedException)
- ⚠️ UseCompose in V3 Builder (not yet added)
- ⚠️ Service async methods (StartAsync, StopAsync, etc.)
- ⚠️ Comprehensive test coverage

---

## Architecture Validation Checklist

- [x] Non-singleton kernel (FluentDockerKernel)
- [x] SysCtl() pattern for driver access
- [x] Driver registration with unique IDs
- [x] Terminal BuildAsync() pattern
- [x] Lambda configuration in builders
- [x] BuildResults with scope tracking
- [x] Error handling with ErrorContext and ErrorCodes
- [x] IComposeDriver implemented
- [ ] Service interfaces with async methods
- [ ] IAsyncDisposable on services
- [ ] Complete unit test coverage
- [ ] Complete integration test coverage

