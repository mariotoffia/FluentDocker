# FluentDocker Migration Plan - Docker CLI Driver Refactoring

## Overview

This document provides a detailed migration plan for refactoring the existing FluentDocker codebase to use the new driver layer architecture, specifically migrating all Docker CLI interactions from the Commands layer to the DockerCliDriver implementation.

**Migration Strategy**: Phased, backward-compatible refactoring that maintains all existing APIs while introducing the driver layer underneath.

---

## Migration Principles

1. **Backward Compatibility**: 100% of existing public APIs must continue to work
2. **Gradual Migration**: Migrate in phases, testing at each step
3. **No Breaking Changes**: Existing user code should require zero modifications
4. **Internal Refactoring**: Changes are internal implementation details
5. **Test Coverage**: Maintain or improve test coverage during migration

---

## Current Architecture Analysis

### Commands Layer Files (to be migrated)

```
Commands/
├── Client.cs              - 22 ProcessExecutor usages (Container operations)
├── Compose.cs             - 17 ProcessExecutor usages (Compose operations)
├── Network.cs             - 6 ProcessExecutor usages (Network operations)
├── Volumes.cs             - 4 ProcessExecutor usages (Volume operations)
├── Images.cs              - 9 ProcessExecutor usages (Image operations)
├── Machine.cs             - 9 ProcessExecutor usages (Docker Machine)
├── Info.cs                - 4 ProcessExecutor usages (System info)
├── Stack.cs               - Swarm stack operations
├── Service.cs             - Swarm service operations
└── Event.cs               - Docker events
```

**Total**: ~71 ProcessExecutor usages to migrate

### Services Layer Files (to be updated)

```
Services/Impl/
├── DockerHostService.cs           - Uses Commands layer
├── DockerContainerService.cs      - Uses Commands layer
├── DockerNetworkService.cs        - Uses Commands layer
├── DockerVolumeService.cs         - Uses Commands layer
├── DockerComposeCompositeService.cs - Uses Commands layer
└── DockerImageService.cs          - Uses Commands layer
```

---

## Phase 1: Foundation (Week 1)

### Step 1.1: Create Driver Infrastructure

**Goal**: Establish the driver layer without changing existing code.

**Tasks**:
1. Create all driver interfaces (IDriver, IContainerDriver, etc.) - See IMPLEMENTATION_PLAN_KERNEL_AND_DRIVERS.md
2. Create driver models (DriverContext, DriverCapabilities, etc.)
3. Create kernel infrastructure (FluentDockerKernel, DriverRegistry, DriverSelector)
4. Add necessary NuGet packages if needed

**Files Created**:
- `Drivers/Core/*.cs` - All driver interfaces
- `Drivers/Models/*.cs` - All driver models
- `Kernel/*.cs` - Kernel infrastructure

**Testing**:
```csharp
[Fact]
public void Kernel_Should_Initialize()
{
    var kernel = FluentDockerKernel.Instance;
    Assert.NotNull(kernel);
    Assert.NotNull(kernel.Registry);
    Assert.NotNull(kernel.Selector);
}

[Fact]
public void Registry_Should_Have_DockerCli_Driver()
{
    var kernel = FluentDockerKernel.Instance;
    var driver = kernel.Registry.GetDriver("docker-cli");
    Assert.NotNull(driver);
    Assert.Equal("docker-cli", driver.Name);
    Assert.Equal(RuntimeType.Docker, driver.Runtime);
}
```

**Validation**: All existing tests should still pass since no existing code is changed yet.

---

## Phase 2: Commands Layer Migration (Week 2)

### Step 2.1: Migrate Commands/Client.cs (Container Operations)

**Current Structure** (`Commands/Client.cs`):
```csharp
public static class Client
{
    public static CommandResponse<string> Start(this DockerUri host, string containerId, ICertificatePaths certificates = null)
    {
        var args = $"{host.RenderBaseArgs(certificates)}";
        return new ProcessExecutor<StringResponseParser, string>(
            "docker".ResolveBinary(),
            $"{args} start {containerId}").Execute();
    }

    // 21 more methods...
}
```

**New Structure** (`Drivers/Docker/Cli/DockerCliContainerDriver.cs`):

Create the driver implementation that encapsulates all container operations:

```csharp
namespace Ductus.FluentDocker.Drivers.Docker.Cli
{
    public class DockerCliContainerDriver : IContainerDriver
    {
        private readonly DockerCliDriver _driver;

        public DockerCliContainerDriver(DockerCliDriver driver)
        {
            _driver = driver;
        }

        public CommandResponse<string> Start(DriverContext context, string containerId)
        {
            var args = $"{context.Host.RenderBaseArgs(context.Certificates)}";
            return new ProcessExecutor<StringResponseParser, string>(
                "docker".ResolveBinary(),
                $"{args} start {containerId}").Execute();
        }

        // Migrate all 22 container operations from Client.cs
    }
}
```

**Update Commands/Client.cs to delegate**:

```csharp
public static class Client
{
    // Keep existing method signatures for backward compatibility
    public static CommandResponse<string> Start(this DockerUri host, string containerId, ICertificatePaths certificates = null)
    {
        // NEW: Delegate to kernel
        var context = DriverContext.FromHost(host, certificates);
        return FluentDockerKernel.Instance.StartContainer(containerId, context);
    }

    // Update all 22 methods to delegate to kernel
}
```

**Migration Mapping**:

| Client.cs Method | DockerCliContainerDriver Method | Kernel Method |
|------------------|----------------------------------|---------------|
| `Start()` | `Start()` | `StartContainer()` |
| `Stop()` | `Stop()` | `StopContainer()` |
| `Create()` | `Create()` | `CreateContainer()` |
| `Remove()` | `Remove()` | `RemoveContainer()` |
| `Pause()` | `Pause()` | - (via driver) |
| `UnPause()` | `Unpause()` | - (via driver) |
| `Kill()` | `Kill()` | - (via driver) |
| `InspectContainer()` | `Inspect()` | `InspectContainer()` |
| `Ps()` | `List()` | `ListContainers()` |
| `Top()` | `Top()` | - (via driver) |
| `Diff()` | `Diff()` | - (via driver) |
| `Execute()` | `Execute()` | - (via driver) |
| `CopyToContainer()` | `CopyTo()` | - (via driver) |
| `CopyFromContainer()` | `CopyFrom()` | - (via driver) |

**Complete Migration Example**:

```csharp
// BEFORE: Commands/Client.cs
public static CommandResponse<string> Stop(this DockerUri host, string containerId,
    int? waitMs = null, ICertificatePaths certificates = null)
{
    var args = $"{host.RenderBaseArgs(certificates)}";
    var timeoutOption = waitMs.HasValue ? $" -t {waitMs.Value / 1000}" : string.Empty;

    return new ProcessExecutor<StringResponseParser, string>(
        "docker".ResolveBinary(),
        $"{args} stop{timeoutOption} {containerId}").Execute();
}

// AFTER: Drivers/Docker/Cli/DockerCliContainerDriver.cs
public CommandResponse<string> Stop(DriverContext context, string containerId, int? waitMs = null)
{
    var args = $"{context.Host.RenderBaseArgs(context.Certificates)}";
    var timeoutOption = waitMs.HasValue ? $" -t {waitMs.Value / 1000}" : string.Empty;

    return new ProcessExecutor<StringResponseParser, string>(
        "docker".ResolveBinary(),
        $"{args} stop{timeoutOption} {containerId}").Execute();
}

// AFTER: Commands/Client.cs (facade)
public static CommandResponse<string> Stop(this DockerUri host, string containerId,
    int? waitMs = null, ICertificatePaths certificates = null)
{
    var context = DriverContext.FromHost(host, certificates);
    return FluentDockerKernel.Instance.StopContainer(containerId, context, waitMs);
}
```

**Testing**:
```csharp
[Fact]
public void Stop_Should_Work_Via_Commands_Layer()
{
    // Existing test - should still pass
    using var container = new Builder().UseContainer().UseImage("alpine").Build().Start();
    var result = container.DockerHost.Stop(container.Id);
    Assert.True(result.Success);
}

[Fact]
public void Stop_Should_Work_Via_Driver()
{
    // New test - via driver directly
    var kernel = FluentDockerKernel.Instance;
    var context = new DriverContext();
    using var container = kernel.CreateContainer(new ContainerCreateParams { Image = "alpine" }, context);
    kernel.StartContainer(container.Data, context);

    var result = kernel.StopContainer(container.Data, context);
    Assert.True(result.Success);
}
```

### Step 2.2: Migrate Commands/Network.cs

**Methods to Migrate** (6 total):
- `NetworkCreate()` → `DockerCliNetworkDriver.Create()`
- `NetworkLs()` → `DockerCliNetworkDriver.List()`
- `NetworkInspect()` → `DockerCliNetworkDriver.Inspect()`
- `NetworkRm()` → `DockerCliNetworkDriver.Remove()`
- `NetworkConnect()` → `DockerCliNetworkDriver.Connect()`
- `NetworkDisconnect()` → `DockerCliNetworkDriver.Disconnect()`

**Implementation**:

```csharp
// Drivers/Docker/Cli/DockerCliNetworkDriver.cs
public class DockerCliNetworkDriver : INetworkDriver
{
    private readonly DockerCliDriver _driver;

    public DockerCliNetworkDriver(DockerCliDriver driver)
    {
        _driver = driver;
    }

    public CommandResponse<string> Create(DriverContext context, NetworkCreateParams createParams)
    {
        var args = $"{context.Host.RenderBaseArgs(context.Certificates)}";

        var options = string.Empty;
        if (!string.IsNullOrEmpty(createParams.Driver))
            options += $" --driver={createParams.Driver}";
        if (createParams.CheckDuplicate)
            options += " --check-duplicate";
        if (createParams.Internal)
            options += " --internal";
        if (createParams.EnableIPv6)
            options += " --ipv6";
        // ... all other options

        var cmd = $"{args} network create{options} {createParams.Name}";
        return new ProcessExecutor<StringResponseParser, string>(
            "docker".ResolveBinary(), cmd).Execute();
    }

    // Migrate other 5 network methods...
}

// Commands/Network.cs (update to delegate)
public static CommandResponse<string> NetworkCreate(this DockerUri host,
    NetworkCreateParams createParams, ICertificatePaths certificates = null)
{
    var context = DriverContext.FromHost(host, certificates);
    return FluentDockerKernel.Instance.CreateNetwork(createParams, context);
}
```

### Step 2.3: Migrate Commands/Images.cs

**Methods to Migrate** (9 total):
- `Pull()` → `DockerCliImageDriver.Pull()`
- `Build()` → `DockerCliImageDriver.Build()`
- `Images()` → `DockerCliImageDriver.List()`
- `InspectImage()` → `DockerCliImageDriver.Inspect()`
- `RemoveImage()` → `DockerCliImageDriver.Remove()`
- `Tag()` → `DockerCliImageDriver.Tag()`
- `Save()` → `DockerCliImageDriver.Save()`
- `Load()` → `DockerCliImageDriver.Load()`
- `History()` → `DockerCliImageDriver.History()`

### Step 2.4: Migrate Commands/Volumes.cs

**Methods to Migrate** (4 total):
- `VolumeCreate()` → `DockerCliVolumeDriver.Create()`
- `VolumeInspect()` → `DockerCliVolumeDriver.Inspect()`
- `VolumeLs()` → `DockerCliVolumeDriver.List()`
- `VolumeRm()` → `DockerCliVolumeDriver.Remove()`

### Step 2.5: Migrate Commands/Compose.cs

**Methods to Migrate** (17 total):
- `ComposeUp()` → `DockerComposeCliDriver.Up()`
- `ComposeDown()` → `DockerComposeCliDriver.Down()`
- `ComposeStart()` → `DockerComposeCliDriver.Start()`
- `ComposeStop()` → `DockerComposeCliDriver.Stop()`
- `ComposeBuild()` → `DockerComposeCliDriver.Build()`
- `ComposePull()` → `DockerComposeCliDriver.Pull()`
- `ComposePs()` → `DockerComposeCliDriver.Ps()`
- `ComposeVersion()` → `DockerComposeCliDriver.Version()`
- ... all 17 methods

**Special Handling for Compose**:

The compose driver needs to handle V1 vs V2 detection:

```csharp
public class DockerComposeCliDriver : IComposeDriver
{
    private readonly DockerCliDriver _driver;
    private readonly DockerBinariesResolver _resolver;

    public DockerComposeCliDriver(DockerCliDriver driver)
    {
        _driver = driver;
        _resolver = new DockerBinariesResolver(SudoMechanism.None, null);
    }

    private (string binary, string command) GetComposeCommand()
    {
        if (_resolver.IsDockerComposeV2Available)
            return ("docker".ResolveBinary(), "compose");
        else
            return ("docker-compose".ResolveBinary(), "");
    }

    public CommandResponse<string> Up(DriverContext context, ComposeUpParams upParams)
    {
        var (binary, command) = GetComposeCommand();
        var args = $"{context.Host.RenderBaseArgs(context.Certificates)}";

        // Build compose command
        var composeCmd = BuildComposeCommand(command, "up", upParams);

        return new ProcessExecutor<StringResponseParser, string>(
            binary, $"{args} {composeCmd}").Execute();
    }

    // ... other compose methods
}
```

### Step 2.6: Migrate Commands/Info.cs (System Operations)

**Methods to Migrate** (4 total):
- `Version()` → `DockerCliSystemDriver.Version()`
- `IsWindowsEngine()` → `DockerCliSystemDriver.IsWindowsEngine()`
- `LinuxDaemon()` → Helper method
- `WindowsDaemon()` → Helper method

---

## Phase 3: Services Layer Migration (Week 3)

### Step 3.1: Update DockerHostService

**Current Implementation**:
```csharp
public class DockerHostService : ServiceBase, IHostService
{
    public IList<IContainerService> GetContainers(bool all = true, params string[] filters)
    {
        // Uses Commands layer
        var response = DockerHost.Ps(all, filters, Certificates);
        if (!response.Success)
            return new List<IContainerService>();

        return response.Data.Select(c =>
            new DockerContainerService(c.Id, c.Image, DockerHost, Certificates, ...)).ToList();
    }
}
```

**New Implementation**:
```csharp
public class DockerHostService : ServiceBase, IHostService
{
    private DriverContext CreateContext()
    {
        return new DriverContext
        {
            Host = DockerHost,
            Certificates = Certificates
        };
    }

    public IList<IContainerService> GetContainers(bool all = true, params string[] filters)
    {
        // Use kernel instead of Commands layer
        var context = CreateContext();
        var response = FluentDockerKernel.Instance.ListContainers(context, all, filters);

        if (!response.Success)
            return new List<IContainerService>();

        return response.Data.Select(c =>
            new DockerContainerService(c.Id, c.Image, DockerHost, Certificates, ...)).ToList();
    }

    public IContainerService Create(ContainerCreateParams createParams)
    {
        var context = CreateContext();
        var response = FluentDockerKernel.Instance.CreateContainer(createParams, context);

        if (!response.Success)
            throw new FluentDockerException($"Failed to create container: {response.Error}");

        return new DockerContainerService(response.Data, createParams.Image, DockerHost, Certificates, ...);
    }

    // Update all other methods to use kernel
}
```

**Migration Pattern for All Services**:

1. Add `CreateContext()` helper method
2. Replace all `DockerHost.XXX()` calls with `FluentDockerKernel.Instance.XXX()`
3. Pass `context` to kernel methods
4. Maintain same return types and error handling

### Step 3.2: Update DockerContainerService

**Methods to Update**:
- `Start()` → Use `kernel.StartContainer()`
- `Stop()` → Use `kernel.StopContainer()`
- `Pause()` → Use driver directly
- `Remove()` → Use `kernel.RemoveContainer()`
- `GetConfiguration()` → Use `kernel.InspectContainer()`

**Example**:
```csharp
public class DockerContainerService : ServiceBase, IContainerService
{
    public override void Start()
    {
        if (State == ServiceRunningState.Running)
            return;

        // OLD: DockerHost.Start(Id, Certificates);
        // NEW:
        var context = new DriverContext
        {
            Host = DockerHost,
            Certificates = Certificates
        };
        var response = FluentDockerKernel.Instance.StartContainer(Id, context);

        if (!response.Success)
            throw new FluentDockerException($"Failed to start container: {response.Error}");

        State = ServiceRunningState.Running;
        OnStateChange(ServiceRunningState.Running);
    }
}
```

### Step 3.3: Update DockerComposeCompositeService

```csharp
public class DockerComposeCompositeService : ServiceBase, ICompositeService
{
    public override void Start()
    {
        if (State == ServiceRunningState.Running)
            return;

        var context = new DriverContext { Host = Hosts.First().Host };
        var upParams = new ComposeUpParams
        {
            WorkingDirectory = _config.WorkingDirectory,
            ComposeFiles = _config.ComposeFilePath.ToArray(),
            ProjectName = _config.AlternativeServiceName,
            // ... map all config options
        };

        var response = FluentDockerKernel.Instance.Compose.Up(context, upParams);

        if (!response.Success)
            throw new FluentDockerException($"Failed to start compose: {response.Error}");

        State = ServiceRunningState.Running;
        OnStateChange(ServiceRunningState.Running);
    }
}
```

---

## Phase 4: Testing and Validation (Week 3)

### Step 4.1: Backward Compatibility Tests

Create comprehensive tests to ensure all existing APIs work:

```csharp
namespace Ductus.FluentDocker.Tests.Migration
{
    public class BackwardCompatibilityTests
    {
        [Fact]
        public void Commands_Layer_Should_Still_Work()
        {
            // Test that Commands layer extensions still work
            var host = new DockerUri("unix:///var/run/docker.sock");
            var response = host.Version();
            Assert.True(response.Success);
        }

        [Fact]
        public void Services_Layer_Should_Still_Work()
        {
            // Test that Services still work
            using var host = new Hosts().Discover().FirstOrDefault();
            var containers = host.GetContainers(all: true);
            Assert.NotNull(containers);
        }

        [Fact]
        public void Fluent_API_Should_Still_Work()
        {
            // Test that fluent builders still work
            using var container = new Builder()
                .UseContainer()
                .UseImage("alpine:latest")
                .Command("sh", "-c", "sleep 10")
                .Build()
                .Start();

            Assert.NotNull(container);
            Assert.Equal(ServiceRunningState.Running, container.State);
        }

        [Fact]
        public void Compose_Should_Still_Work()
        {
            var file = Path.Combine(Directory.GetCurrentDirectory(), "docker-compose.yml");

            using var svc = new Builder()
                .UseContainer()
                .UseCompose()
                .FromFile(file)
                .Build()
                .Start();

            Assert.NotNull(svc);
            Assert.NotEmpty(svc.Containers);
        }
    }
}
```

### Step 4.2: Driver Tests

Test drivers directly:

```csharp
public class DockerCliDriverTests
{
    [Fact]
    public void Driver_Should_Be_Available()
    {
        var driver = new DockerCliDriver();
        var context = new DriverContext();
        Assert.True(driver.IsAvailable(context));
    }

    [Fact]
    public void Container_Driver_Should_Create_Container()
    {
        var driver = new DockerCliDriver();
        var context = new DriverContext();
        var createParams = new ContainerCreateParams
        {
            Image = "alpine:latest",
            Command = new[] { "sh", "-c", "sleep 10" }
        };

        var response = driver.Containers.Create(context, createParams);
        Assert.True(response.Success);
        Assert.NotEmpty(response.Data);

        // Cleanup
        driver.Containers.Remove(context, response.Data, force: true);
    }
}
```

### Step 4.3: Kernel Tests

```csharp
public class FluentDockerKernelTests
{
    [Fact]
    public void Kernel_Should_Auto_Register_Docker_CLI_Driver()
    {
        var kernel = FluentDockerKernel.Instance;
        var driver = kernel.Registry.GetDriver("docker-cli");
        Assert.NotNull(driver);
    }

    [Fact]
    public void Kernel_Should_Select_Appropriate_Driver()
    {
        var kernel = FluentDockerKernel.Instance;
        var context = new DriverContext
        {
            Preferences = new DriverPreferences
            {
                TargetRuntime = RuntimeType.Docker,
                PreferredType = PreferredDriverType.CLI
            }
        };

        var driver = kernel.Selector.SelectDriver(context);
        Assert.NotNull(driver);
        Assert.Equal("docker-cli", driver.Name);
    }

    [Fact]
    public void Kernel_Should_Execute_Container_Operations()
    {
        var kernel = FluentDockerKernel.Instance;
        var context = new DriverContext();

        var createParams = new ContainerCreateParams { Image = "alpine" };
        var createResponse = kernel.CreateContainer(createParams, context);
        Assert.True(createResponse.Success);

        var startResponse = kernel.StartContainer(createResponse.Data, context);
        Assert.True(startResponse.Success);

        var stopResponse = kernel.StopContainer(createResponse.Data, context);
        Assert.True(stopResponse.Success);

        var removeResponse = kernel.RemoveContainer(createResponse.Data, context);
        Assert.True(removeResponse.Success);
    }
}
```

---

## Phase 5: Documentation Updates (Week 4)

### Step 5.1: Update README.md

Add new section about driver architecture:

```markdown
## Driver Architecture

FluentDocker now supports multiple container runtime drivers:

- **Docker CLI Driver** - Uses docker command-line interface (default)
- **Docker API Driver** - Uses Docker Engine API via Docker.DotNet
- **Podman CLI Driver** - Uses podman command-line interface

### Using Drivers

Drivers are automatically selected based on available runtimes:

```csharp
// Default behavior - auto-select best driver
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();

// Explicit driver selection
var context = new DriverContext
{
    Preferences = new DriverPreferences
    {
        TargetRuntime = RuntimeType.Docker,
        PreferredType = PreferredDriverType.API  // Prefer API over CLI
    }
};

var kernel = FluentDockerKernel.Instance;
var container = kernel.CreateContainer(createParams, context);
```

### Driver Capabilities

Each driver exposes its capabilities:

```csharp
var driver = kernel.Registry.GetDriver("docker-cli");
var capabilities = driver.GetCapabilities();

if (capabilities.SupportsBuildx)
{
    // Use buildx features
}
```
```

### Step 5.2: Create Migration Guide

**File**: `docs/MIGRATION_GUIDE.md`

```markdown
# Migration Guide - Driver Architecture

## For Users

### No Changes Required

If you're using FluentDocker as a library, **no code changes are required**. The driver architecture is transparent to users. All existing APIs continue to work exactly as before.

### Performance Improvements

The new architecture may improve performance for certain operations when using API-based drivers instead of CLI-based drivers.

### Multi-Runtime Support

You can now target different container runtimes (Docker, Podman) by specifying driver preferences:

```csharp
var context = new DriverContext
{
    Preferences = new DriverPreferences { TargetRuntime = RuntimeType.Podman }
};
```

## For Contributors

### Architecture Changes

The codebase now has these new layers:

1. **Drivers Layer** (`Drivers/`) - Pluggable driver implementations
2. **Kernel Layer** (`Kernel/`) - Driver coordination and selection
3. **Commands Layer** (updated) - Now a facade over drivers
4. **Services Layer** (updated) - Uses kernel instead of Commands
5. **Builders Layer** (unchanged) - No changes needed

### Adding New Drivers

To add a new driver:

1. Implement `IDriver` interface
2. Implement specialized interfaces (IContainerDriver, etc.)
3. Register with kernel in `FluentDockerKernel.AutoRegisterDrivers()`

See `Drivers/Docker/Cli/DockerCliDriver.cs` for reference implementation.
```

---

## Migration Checklist

### Phase 1: Foundation ✓
- [ ] Create driver interfaces
- [ ] Create driver models
- [ ] Create kernel infrastructure
- [ ] Add unit tests for kernel
- [ ] Verify all existing tests still pass

### Phase 2: Commands Layer Migration
- [ ] Migrate `Commands/Client.cs` (22 methods)
- [ ] Migrate `Commands/Network.cs` (6 methods)
- [ ] Migrate `Commands/Images.cs` (9 methods)
- [ ] Migrate `Commands/Volumes.cs` (4 methods)
- [ ] Migrate `Commands/Compose.cs` (17 methods)
- [ ] Migrate `Commands/Info.cs` (4 methods)
- [ ] Update all Commands methods to delegate to kernel
- [ ] Add driver unit tests
- [ ] Verify backward compatibility

### Phase 3: Services Layer Migration
- [ ] Update `DockerHostService.cs`
- [ ] Update `DockerContainerService.cs`
- [ ] Update `DockerNetworkService.cs`
- [ ] Update `DockerVolumeService.cs`
- [ ] Update `DockerComposeCompositeService.cs`
- [ ] Update `DockerImageService.cs`
- [ ] Add integration tests
- [ ] Verify backward compatibility

### Phase 4: Testing
- [ ] Run full test suite
- [ ] Add backward compatibility tests
- [ ] Add driver-specific tests
- [ ] Add kernel tests
- [ ] Performance testing
- [ ] Integration testing

### Phase 5: Documentation
- [ ] Update README.md
- [ ] Create MIGRATION_GUIDE.md
- [ ] Update API documentation
- [ ] Add driver examples
- [ ] Update architecture diagrams

---

## Rollback Plan

If issues are discovered:

1. **Immediate Rollback**: Revert Commands layer changes to delegate back to original ProcessExecutor calls
2. **Service Rollback**: Revert Services to use Commands layer directly
3. **Keep Infrastructure**: Keep kernel and driver infrastructure for future use

**Rollback is low-risk** because:
- All changes are internal implementation
- Public APIs remain unchanged
- Driver layer is additive, not replacement

---

## Success Criteria

Migration is successful when:

1. ✅ All existing tests pass
2. ✅ All existing examples work unchanged
3. ✅ Performance is equal or better
4. ✅ New driver tests pass
5. ✅ Backward compatibility tests pass
6. ✅ Documentation is updated
7. ✅ Code coverage is maintained or improved

---

## Timeline Summary

- **Week 1**: Foundation (kernel, registry, selector, interfaces)
- **Week 2**: Commands layer migration (all 71 ProcessExecutor usages)
- **Week 3**: Services layer migration and testing
- **Week 4**: Documentation and final validation

**Total Duration**: 4 weeks for complete Docker CLI driver migration

After this migration is complete, we can proceed with:
- Docker API driver implementation (2 weeks)
- Podman CLI driver implementation (2 weeks)
- Advanced features and optimizations (2 weeks)
