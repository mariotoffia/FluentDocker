# Migrating to FluentDocker v3.0.0

> **⚠️ IMPORTANT: This guide helps you migrate from v2.x.x to v3.0.0**

FluentDocker v3.0.0 is a major release with significant architectural improvements and some breaking changes. This guide will help you migrate your code smoothly.

---

## Table of Contents

- [Quick Summary](#quick-summary)
- [Critical Breaking Changes](#critical-breaking-changes)
  - [1. Namespace Rename](#1-namespace-rename)
  - [2. Docker Compose Commands](#2-docker-compose-commands)
- [Major Changes](#major-changes)
  - [3. Builder Scoping Pattern](#3-builder-scoping-pattern)
  - [4. Commands Namespace Deprecation](#4-commands-namespace-deprecation)
  - [5. Service Interface Changes](#5-service-interface-changes)
- [New Features](#new-features)
- [Migration Path](#migration-path)
- [Detailed Guides](#detailed-guides)

---

## Quick Summary

### What Changed?

| Category | Change | Impact | Action Required |
|----------|--------|--------|-----------------|
| **Namespace** | `Ductus.FluentDocker` → `FluentDocker` | 🔴 **HIGH** | **REQUIRED**: Update all using statements |
| **NuGet Packages** | Package names changed | 🔴 **HIGH** | **REQUIRED**: Update package references |
| **Compose Commands** | Struct-based arguments | 🔴 **HIGH** | **REQUIRED** if using Compose commands directly |
| **Builder API** | New `WithinDriver()` pattern | 🟡 MEDIUM | Optional: Use default kernel for simple cases |
| **Service Properties** | `DockerHost` → `Context.Host` | 🟡 MEDIUM | Update if accessing these properties |
| **Commands Layer** | Deprecated | 🟢 LOW | Migrate to Drivers (deprecation warnings only) |

### Migration Time Estimate

- **Simple projects** (basic Builder API): 15-30 minutes
- **Medium projects** (Compose + multiple hosts): 1-2 hours
- **Complex projects** (Commands layer + custom services): 2-4 hours

---

## Critical Breaking Changes

These changes **MUST** be addressed for your code to compile and run.

### 1. Namespace Rename

**⚠️ ALL PROJECTS AFFECTED**

The package and namespace have been renamed from `Ductus.FluentDocker` to `FluentDocker`.

#### NuGet Package Changes

Update your `.csproj` file:

```xml
<!-- ❌ OLD (v2.x.x) -->
<PackageReference Include="Ductus.FluentDocker" Version="2.x.x" />
<PackageReference Include="Ductus.FluentDocker.MsTest" Version="2.x.x" />
<PackageReference Include="Ductus.FluentDocker.XUnit" Version="2.x.x" />

<!-- ✅ NEW (v3.0.0) -->
<PackageReference Include="FluentDocker" Version="3.0.0" />
<PackageReference Include="FluentDocker.MsTest" Version="3.0.0" />
<PackageReference Include="FluentDocker.XUnit" Version="3.0.0" />
```

#### Namespace Changes

Update all using statements:

```csharp
// ❌ OLD (v2.x.x)
using Ductus.FluentDocker;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Model.Containers;

// ✅ NEW (v3.0.0)
using FluentDocker;
using FluentDocker.Builders;
using FluentDocker.Services;
using FluentDocker.Model.Containers;
```

#### Automated Migration

**Visual Studio / Rider:**
- Find: `Ductus.FluentDocker`
- Replace: `FluentDocker`
- Scope: Entire Solution

**Command Line (macOS/Linux):**
```bash
# Update .cs files
find . -name "*.cs" -exec sed -i '' 's/Ductus\.FluentDocker/FluentDocker/g' {} \;

# Update .csproj files
find . -name "*.csproj" -exec sed -i '' 's/Ductus\.FluentDocker/FluentDocker/g' {} \;
```

**PowerShell (Windows):**
```powershell
# Update .cs files
Get-ChildItem -Recurse -Filter *.cs | ForEach-Object {
    (Get-Content $_.FullName) -replace 'Ductus\.FluentDocker', 'FluentDocker' | Set-Content $_.FullName
}

# Update .csproj files
Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object {
    (Get-Content $_.FullName) -replace 'Ductus\.FluentDocker', 'FluentDocker' | Set-Content $_.FullName
}
```

#### Logging Configuration

If you have logging configuration filtering by namespace, update it:

```json
// ❌ OLD
{
  "Logging": {
    "LogLevel": {
      "Ductus.FluentDocker": "Debug"
    }
  }
}

// ✅ NEW
{
  "Logging": {
    "LogLevel": {
      "FluentDocker": "Debug"
    }
  }
}
```

---

### 2. Docker Compose Commands

**⚠️ AFFECTS: Projects using Compose commands directly**

All Docker Compose commands now use **struct-based arguments** instead of individual parameters.

#### Old vs New API

```csharp
// ❌ OLD (v2.x.x) - Multiple parameters
host.Host.ComposeBuild(
    altProjectName: "myproject",
    forceRm: true,
    dontUseCache: true,
    alwaysPull: false,
    services: new[] { "web", "db" },
    env: null,
    certificates: host.Certificates,
    composeFile: "docker-compose.yml"
);

// ✅ NEW (v3.0.0) - Struct-based
host.Host.ComposeBuildCommand(new ComposeBuildCommandArgs
{
    AltProjectName = "myproject",
    ForceRm = true,
    NoCache = true,
    Pull = false,
    Services = new[] { "web", "db" },
    Certificates = host.Certificates,
    ComposeFiles = new[] { "docker-compose.yml" }
});
```

#### Complete Mapping Table

| Old Method (v2.x.x) | New Method (v3.0.0) | Struct Type |
|---------------------|---------------------|-------------|
| `ComposeBuild(...)` | `ComposeBuildCommand(args)` | `ComposeBuildCommandArgs` |
| `ComposeCreate(...)` | `ComposeCreateCommand(args)` | `ComposeCreateCommandArgs` |
| `ComposeStart(...)` | `ComposeStartCommand(args)` | `ComposeStartCommandArgs` |
| `ComposeStop(...)` | `ComposeStopCommand(args)` | `ComposeStopCommandArgs` |
| `ComposeKill(...)` | `ComposeKillCommand(args)` | `ComposeKillCommandArgs` |
| `ComposePause(...)` | `ComposePauseCommand(args)` | `ComposePauseCommandArgs` |
| `ComposeUnPause(...)` | `ComposeUnpauseCommand(args)` | `ComposeUnpauseCommandArgs` |
| `ComposeScale(...)` | `ComposeScaleCommand(args)` | `ComposeScaleCommandArgs` |
| `ComposeDown(...)` | `ComposeDownCommand(args)` | `ComposeDownCommandArgs` |
| `ComposeUp(...)` | `ComposeUpCommand(args)` | `ComposeUpCommandArgs` |
| `ComposeRm(...)` | `ComposeRmCommand(args)` | `ComposeRmCommandArgs` |
| `ComposePs(...)` | `ComposePsCommand(args)` | `ComposePsCommandArgs` |

#### New Methods in v3.0.0

These commands are new and have no v2.x.x equivalent:

- `ComposeExecCommand` - Execute command in running service
- `ComposeRunCommand` - Run one-off command
- `ComposeTopCommand` - Display running processes
- `ComposeImagesCommand` - List images
- `ComposeCpCommand` - Copy files
- `ComposeLogsCommand` - View logs

#### Why the Change?

1. **Extensibility**: Add new options without breaking existing code
2. **Discoverability**: IntelliSense shows all available options
3. **Type Safety**: Named properties prevent parameter order mistakes
4. **Readability**: Clear what each argument does

---

## Major Changes

These changes have lower impact but provide significant improvements.

### 3. Builder Scoping Pattern

**Change**: Builder now supports explicit kernel management with `WithinDriver()` scoping.

#### Simple Cases (No Change Required)

For basic use cases, **no changes are needed** - the Builder uses a default kernel:

```csharp
// ✅ Works in both v2.x.x and v3.0.0
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();
```

#### Advanced Cases (Explicit Kernel)

For better control, use explicit kernel management:

```csharp
// ❌ OLD (v2.x.x) - Static host discovery
var hosts = new Hosts().Discover();
var docker = hosts.FirstOrDefault(x => x.IsNative);

using var container = new Builder()
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();

// ✅ NEW (v3.0.0) - Explicit kernel
var kernel = await FluentDockerKernel.Create()
    .WithDriver("docker", d => d.UseDockerCli().AsDefault())
    .BuildAsync();

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer()
            .UseImage("nginx")
            .BuildAsync();

var container = results.GetContainer("nginx");
await container.StartAsync();
```

#### Benefits of Explicit Kernel

1. **Multiple hosts**: Manage Docker, Podman, or remote hosts simultaneously
2. **Better testing**: Mock drivers for unit tests
3. **Resource control**: Explicit lifetime management
4. **Configuration**: Control driver selection and fallback

---

### 4. Commands Namespace Deprecation

**⚠️ DEPRECATION WARNING**: The `FluentDocker.Commands` namespace is deprecated and will be removed in v4.0.0.

#### Why Deprecated?

The Commands layer was the original API. It has been replaced by the **Driver Layer** which provides:

- ✅ Async/await support
- ✅ Type-safe error handling (`CommandResponse<T>`)
- ✅ Pluggable architecture (Docker, Podman, Kubernetes)
- ✅ Better testability (mockable drivers)

#### Migration Strategy

**Option 1: Continue using v2 Services** (Easiest)

The v2 service layer (`DockerContainerService`, etc.) continues to work in v3.0.0. You'll see deprecation warnings, but functionality is preserved.

```csharp
// ⚠️ Works but deprecated
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx")
    .Build()  // Uses v2 services internally
    .Start();
```

**Option 2: Use v3 Async Services** (Recommended)

Migrate to the new async service layer:

```csharp
// ❌ OLD (v2.x.x Commands)
using FluentDocker.Commands;

var result = host.Ps("--all", certificates);
var container = host.InspectContainer(containerId, certificates);
host.Start(containerId, certificates);

// ✅ NEW (v3.0.0 Drivers)
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

var driver = kernel.SysCtl<IContainerDriver>("docker");
var context = new DriverContext("docker");

var result = await driver.ListAsync(context, new ContainerListFilter { All = true });
var container = await driver.InspectAsync(context, containerId);
await driver.StartAsync(context, containerId);
```

#### Commands → Driver Mapping

| Commands Class | Driver Interface |
|----------------|-----------------|
| `Client` | `IContainerDriver` |
| `Images` | `IImageDriver` |
| `Network` | `INetworkDriver` |
| `Volumes` | `IVolumeDriver` |
| `Info` | `ISystemDriver` |
| `Compose` | `IComposeDriver` |

---

### 5. Service Interface Changes

**Change**: Services now reference `FluentDockerKernel` and `DriverContext` instead of `DockerUri` and `ICertificatePaths`.

#### Property Access Changes

```csharp
// ❌ OLD (v2.x.x)
var host = container.DockerHost;
var certs = container.Certificates;

// ✅ NEW (v3.0.0)
var host = container.Context.Host;
var certs = container.Context.Certificates;

// NEW: Additional properties
var kernel = container.Kernel;
var driverId = container.DriverId;
var context = container.Context;
```

#### Interface Changes

```csharp
// ❌ OLD (v2.x.x)
public interface IContainerService : IService
{
    DockerUri DockerHost { get; }
    ICertificatePaths Certificates { get; }
}

// ✅ NEW (v3.0.0)
public interface IContainerService : IService
{
    FluentDockerKernel Kernel { get; }
    string DriverId { get; }
    DriverContext Context { get; }
}
```

---

## New Features

v3.0.0 introduces powerful new features:

### 1. Label-Based Container Filtering

**NEW**: Server-side label filtering for efficient container management.

```csharp
// Filter containers by label
var filter = new ContainerListFilter
{
    All = true,
    Labels = new Dictionary<string, string>
    {
        ["app"] = "web",
        ["env"] = "test"
    }
};

var result = await driver.ListAsync(context, filter);
```

**Test Utilities**: Automatic cleanup of test containers using labels.

```csharp
using FluentDocker.Tests.Utilities;

// Create labeled test containers
var labels = TestContainerUtils.CreateTestLabels(sessionId);
var container = await driver.RunAsync(context, new ContainerCreateConfig
{
    Image = "alpine:latest",
    Labels = labels,  // Automatically tagged for cleanup
    Detach = true
});

// Cleanup all test containers
await TestContainerUtils.RemoveTestContainersByLabelAsync(kernel, driverId, sessionId);
```

**Benefits**:
- ⚡ 5.5x faster cleanup (server-side filtering)
- 🎯 Exact label matching (no name prefix guessing)
- 🧹 Automatic test resource cleanup
- 🔧 Custom labels for organization

See [.temp-files/docs/label-based-cleanup.md](.temp-files/docs/label-based-cleanup.md) for complete documentation.

### 2. Multiple Driver Instances

**NEW**: Register and use multiple drivers simultaneously.

```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker-local", d => d.UseDockerCli().AsDefault())
    .WithDriver("docker-staging", d => d.UseDockerApi("tcp://staging:2376", stagingCerts))
    .WithDriver("podman", d => d.UsePodmanCli())
    .Build();

// Deploy to different environments
var localContainer = await new Builder()
    .WithinDriver("docker-local", kernel)
        .UseContainer()
            .UseImage("nginx")
            .BuildAsync();

var stagingContainer = await new Builder()
    .WithinDriver("docker-staging", kernel)
        .UseContainer()
            .UseImage("myapp:latest")
            .BuildAsync();
```

### 3. Async/Await First

**NEW**: All operations support async/await for better performance.

```csharp
// ❌ OLD (v2.x.x) - Blocking
var container = new Builder()
    .UseContainer()
    .UseImage("postgres")
    .Build()
    .Start();

// ✅ NEW (v3.0.0) - Async
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer()
            .UseImage("postgres")
            .BuildAsync();

var container = results.GetContainer("postgres");
await container.StartAsync();
```

### 4. Type-Safe Error Handling

**NEW**: Specific exception types and error codes.

```csharp
// ❌ OLD (v2.x.x)
try
{
    var container = builder.Build();
}
catch (FluentDockerException ex)
{
    // Can't determine specific error type
    Console.WriteLine(ex.Message);
}

// ✅ NEW (v3.0.0)
try
{
    var results = await builder.BuildAsync();
}
catch (ImageNotFoundException ex)
{
    Console.WriteLine($"Image not found: {ex.ImageName}");
    Console.WriteLine($"Error Code: {ex.ErrorCode}");
}
catch (ContainerCreationException ex)
{
    Console.WriteLine($"Failed to create container: {ex.Message}");
    Console.WriteLine($"Context: {ex.Context}");
}
```

### 5. Enhanced ContainerListFilter

**NEW**: Full filtering support for all Docker ps filters.

```csharp
var filter = new ContainerListFilter
{
    All = true,
    Status = "exited",
    Labels = new Dictionary<string, string>
    {
        ["fluentdocker.test"] = "true"
    },
    Name = "my-container",
    Ancestor = "nginx:latest"
};

var containers = await driver.ListAsync(context, filter);
```

Supported filters:
- **Labels** - One or more labels
- **Name** - Container name
- **Status** - running, paused, exited, etc.
- **Id** - Container ID
- **Ancestor** - Image name/ID
- **All** - Include stopped containers

---

## Migration Path

### Phase 1: Critical Changes (Required)

1. **Update NuGet packages** - Remove `Ductus.` prefix
2. **Update namespaces** - Find & replace `Ductus.FluentDocker` → `FluentDocker`
3. **Update Compose commands** - Change to struct-based arguments (if using Compose)
4. **Update logging** - If filtering by namespace

**Time**: 15-30 minutes

### Phase 2: Property Access (If Needed)

1. **Update service properties** - `DockerHost` → `Context.Host`
2. **Update certificate access** - `Certificates` → `Context.Certificates`

**Time**: 15-30 minutes

### Phase 3: Adopt New Features (Optional)

1. **Add label-based cleanup** - For test projects
2. **Use explicit kernel** - For better control
3. **Migrate to drivers** - From Commands layer
4. **Use async services** - For better performance

**Time**: 1-4 hours (depending on project complexity)

---

## Detailed Guides

For detailed information on specific topics:

- **Complete Migration Guide**: [docs/architecture/MIGRATION_GUIDE_V3.md](docs/architecture/MIGRATION_GUIDE_V3.md)
- **Error Handling Migration**: [docs/architecture/ERROR_HANDLING_MIGRATION_V3.md](docs/architecture/ERROR_HANDLING_MIGRATION_V3.md)
- **Driver Architecture**: [docs/architecture/DRIVER_LAYER_ARCHITECTURE_V3.md](docs/architecture/DRIVER_LAYER_ARCHITECTURE_V3.md)
- **Fluent API**: [docs/architecture/FLUENT_API_AND_CAPABILITIES_V3.md](docs/architecture/FLUENT_API_AND_CAPABILITIES_V3.md)
- **Test Comparison**: [FluentDocker.Tests/TEST_COMPARISON.md](FluentDocker.Tests/TEST_COMPARISON.md)

---

## Getting Help

If you encounter issues during migration:

1. Check the [detailed guides](#detailed-guides) above
2. Review the [examples](Examples/) directory
3. Open an issue on [GitHub](https://github.com/mariotoffia/FluentDocker/issues)

---

## Summary

### Migration Checklist

- [ ] Update NuGet package references (remove `Ductus.` prefix)
- [ ] Find & replace `Ductus.FluentDocker` → `FluentDocker` in all files
- [ ] Update logging configuration (if applicable)
- [ ] Update Compose commands to struct-based arguments (if using Compose)
- [ ] Update service property access (if accessing `DockerHost`/`Certificates`)
- [ ] Test your application
- [ ] (Optional) Adopt new features (labels, async, etc.)

### Benefits of v3.0.0

✅ **Cleaner namespace**: Simplified from `Ductus.FluentDocker` to `FluentDocker`
✅ **Async/await support**: Full async operations for better performance
✅ **Multiple runtimes**: Docker and Podman simultaneously
✅ **Multiple hosts**: Manage multiple Docker hosts easily
✅ **Label filtering**: 5.5x faster container cleanup
✅ **Better testing**: Mock drivers, isolated kernels
✅ **Type-safe errors**: Specific exceptions with error codes
✅ **More flexible**: Driver plugins, custom implementations

### Migration Effort

| Project Type | Estimated Time | Difficulty |
|-------------|----------------|------------|
| Simple (basic Builder) | 15-30 min | 🟢 Easy |
| Medium (Compose + Builder) | 1-2 hours | 🟡 Medium |
| Complex (Commands + Custom) | 2-4 hours | 🟠 Hard |

**The migration is designed to be smooth and incremental** - start with the required changes, then adopt new features as needed.
