# Migration Guide: FluentDocker v2.x.x → v3.0.0

## Overview

FluentDocker v3.0.0 introduces a pluggable driver layer architecture with **breaking changes**. This guide helps you migrate from v2.x.x to v3.0.0.

**Key Changes:**
- **⚠️ Package and namespace renamed from `Ductus.FluentDocker` to `FluentDocker`**
- **Builder uses WithinDriver() scoping pattern** (no kernel in constructor)
- **Scoped operations with kernel reuse**
- **BuildResults for tracking multi-scope deployments**
- **Services reference kernel instead of DockerUri**
- **Multiple driver instances supported**
- **SysCtl() interface for driver access**
- **DriverContext replaces DockerUri + ICertificatePaths in some APIs**

---

## Quick Start

### Simple Migration with WithinDriver()

**v2.x.x**:
```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();
```

**v3.0.0**:
```csharp
// Create kernel and register driver
var kernel = new FluentDockerKernel();
kernel.RegisterDriver("docker", new DockerCliDriver());

// Use WithinDriver() to scope operations
using var container = new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer()
            .UseImage("nginx")
            .BuildAndGet();  // BuildAndGet() returns service
container.Start();
```

### Fluent Kernel Configuration (Recommended)

**v3.0.0**:
```csharp
// Create kernel with fluent API
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker")
        .UseDockerCli()
        .Build()
    .Build();

// Use scoped builder
using var container = new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer()
            .UseImage("nginx")
            .BuildAndGet();
container.Start();
```

---

## Breaking Changes

### 0. Package and Namespace Rename

**⚠️ CRITICAL BREAKING CHANGE**

In v3.0.0, the package and namespace have been simplified from `Ductus.FluentDocker` to `FluentDocker`.

#### NuGet Package Changes

| v2.x.x Package | v3.0.0 Package |
|----------------|----------------|
| `Ductus.FluentDocker` | `FluentDocker` |
| `Ductus.FluentDocker.MsTest` | `FluentDocker.MsTest` |
| `Ductus.FluentDocker.XUnit` | `FluentDocker.XUnit` |

**Migration Step**: Update your package references in your `.csproj` file:

```xml
<!-- v2.x.x -->
<PackageReference Include="Ductus.FluentDocker" Version="2.x.x" />
<PackageReference Include="Ductus.FluentDocker.MsTest" Version="2.x.x" />
<PackageReference Include="Ductus.FluentDocker.XUnit" Version="2.x.x" />

<!-- v3.0.0 -->
<PackageReference Include="FluentDocker" Version="3.0.0" />
<PackageReference Include="FluentDocker.MsTest" Version="3.0.0" />
<PackageReference Include="FluentDocker.XUnit" Version="3.0.0" />
```

#### Namespace Changes

All namespaces have been renamed from `Ductus.FluentDocker.*` to `FluentDocker.*`.

**v2.x.x**:
```csharp
using Ductus.FluentDocker;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
```

**v3.0.0**:
```csharp
using FluentDocker;
using FluentDocker.Builders;
using FluentDocker.Commands;
using FluentDocker.Extensions;
using FluentDocker.Model.Containers;
using FluentDocker.Services;
```

#### Quick Find & Replace Migration

You can migrate your code with a simple find-and-replace:

**Using IDE**:
1. Find: `Ductus.FluentDocker`
2. Replace with: `FluentDocker`

**Using Command Line (Unix/macOS)**:
```bash
# For all .cs files in current directory recursively
find . -name "*.cs" -exec sed -i '' 's/Ductus\.FluentDocker/FluentDocker/g' {} \;

# For .csproj files
find . -name "*.csproj" -exec sed -i '' 's/Ductus\.FluentDocker/FluentDocker/g' {} \;
```

**Using PowerShell (Windows)**:
```powershell
# For all .cs files
Get-ChildItem -Recurse -Filter *.cs | ForEach-Object {
    (Get-Content $_.FullName) -replace 'Ductus\.FluentDocker', 'FluentDocker' | Set-Content $_.FullName
}

# For .csproj files
Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object {
    (Get-Content $_.FullName) -replace 'Ductus\.FluentDocker', 'FluentDocker' | Set-Content $_.FullName
}
```

#### Logging Category Change

The logging category has also changed:

| v2.x.x | v3.0.0 |
|--------|--------|
| `Ductus.FluentDocker` | `FluentDocker` |

If you have logging configuration filtering by namespace, update accordingly:

```json
// v2.x.x appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Ductus.FluentDocker": "Debug"
    }
  }
}

// v3.0.0 appsettings.json
{
  "Logging": {
    "LogLevel": {
      "FluentDocker": "Debug"
    }
  }
}
```

---

### Commands Namespace Deprecation

**⚠️ DEPRECATION WARNING**

The `FluentDocker.Commands` namespace is **deprecated** in v3.0.0 and will be removed in v4.0.0. All classes in this namespace have been marked with `[Obsolete]` attributes.

#### Why is Commands Deprecated?

The Commands namespace was the original v1/v2 API for executing Docker CLI commands. It has been superseded by the **Driver Layer** which provides:

- **Async/await support** - Better performance and scalability
- **Type-safe error handling** - `CommandResponse<T>` with error codes instead of exceptions
- **Pluggable architecture** - Support for Docker CLI, Podman, Kubernetes, and future runtimes
- **Testability** - Driver interfaces enable mocking and testing

#### Migration from Commands to Drivers

**v2.x.x (Commands)**:
```csharp
using FluentDocker.Commands;

// Direct CLI calls via extension methods
var result = host.Ps("--all", certificates);
var container = host.InspectContainer(containerId, certificates);
host.Start(containerId, certificates);
host.Stop(containerId, null, certificates);
```

**v3.0.0 (Drivers - Recommended)**:
```csharp
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

// Get driver from kernel
var driver = kernel.GetDriver<IContainerDriver>("docker");
var context = new DriverContext("default") { Host = "unix:///var/run/docker.sock" };

// Async driver calls with typed responses
var result = await driver.ListAsync(context, new ContainerListFilter { All = true });
var container = await driver.InspectAsync(context, containerId);
await driver.StartAsync(context, containerId);
await driver.StopAsync(context, containerId, timeout: 30);
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
| `Machine` | `IMachineDriver` |
| `Service` | `IServiceDriver` |
| `Stack` | `IStackDriver` |
| `ClientStreams` | `IStreamDriver` |
| `ComposeStreams` | `IStreamDriver` |

#### Gradual Migration Strategy

1. **Phase 1**: Continue using v2 Services/Builders (which internally use Commands) - works in v3.0.0 with deprecation warnings
2. **Phase 2**: Migrate to v3 async Services (`FluentDocker.Services.V3`) which use the Driver layer:
   - `ContainerServiceAsync` - replaces `DockerContainerService`
   - `ImageServiceAsync` - replaces `DockerImageService`
   - `NetworkServiceAsync` - replaces `DockerNetworkService`
   - `VolumeServiceAsync` - replaces `DockerVolumeService`
   - `HostServiceAsync` - replaces `DockerHostService`
   - `ComposeServiceAsync` - replaces `DockerComposeCompositeService`
   - `EngineScopeAsync` - replaces `EngineScope`
3. **Phase 3**: For advanced use cases, call Driver interfaces directly

**Note**: The v2 Services (`DockerContainerService`, `DockerHostService`, etc.) continue to work in v3.0.0 and use Commands internally. You will see deprecation warnings, but functionality is preserved.

---

### 1. Builder Scoping Pattern

**Change**: `Builder` no longer takes kernel in constructor. Use `WithinDriver()` to establish scopes.

**v2.x.x**:
```csharp
var builder = new Builder();
using var container = builder
    .UseContainer()
    .UseImage("nginx")
    .Build();
```

**v3.0.0**:
```csharp
var kernel = new FluentDockerKernel();
kernel.RegisterDriver("docker", new DockerCliDriver());

// WithinDriver() establishes the scope
var builder = new Builder();
using var container = builder
    .WithinDriver("docker", kernel)
        .UseContainer()
            .UseImage("nginx")
            .BuildAndGet();  // BuildAndGet() returns service
```

**Migration**:
1. Create and configure kernel with drivers
2. Use `WithinDriver(driverId, kernel)` before builder operations
3. Use `BuildAndGet()` to get service directly, or `Build()` + `GetResults()` for multi-scope deployments

### 2. Service Interfaces

**Change**: Services now reference `FluentDockerKernel` and `DriverContext` instead of `DockerUri` and `ICertificatePaths`.

**v2.x.x** (`IContainerService`):
```csharp
public interface IContainerService : IService
{
    DockerUri DockerHost { get; }
    ICertificatePaths Certificates { get; }
    // ...
}
```

**v3.0.0** (`IContainerService`):
```csharp
public interface IContainerService : IService
{
    FluentDockerKernel Kernel { get; }
    string DriverId { get; }
    DriverContext Context { get; }
    // DockerHost and Certificates are in Context:
    // - Context.Host (was DockerHost)
    // - Context.Certificates (was Certificates)
}
```

**Migration**:
```csharp
// v2.x.x
var host = container.DockerHost;
var certs = container.Certificates;

// v3.0.0
var host = container.Context.Host;
var certs = container.Context.Certificates;

// Also available:
var kernel = container.Kernel;
var driverId = container.DriverId;
```

### 3. Custom Host Configuration

**Change**: Host configuration now done via driver registration.

**v2.x.x**:
```csharp
var host = new DockerUri("tcp://remote:2376");
var certs = new CertificatePaths("path/to/certs");

using var container = new Builder()
    .UseHost()
    .WithHostUri(host)
    .WithCertificates(certs)
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();
```

**v3.0.0**:
```csharp
var kernel = new FluentDockerKernel(new FluentDockerKernelOptions
{
    AutoRegisterDrivers = false  // Manual registration
});

// Register driver with host/certs
kernel.RegisterDriver("remote-docker", new DockerCliDriver(
    new DockerUri("tcp://remote:2376"),
    new CertificatePaths("path/to/certs")
));

using var container = new Builder(kernel)
    .UseContainer()
    .UseDriver("remote-docker")  // Specify which driver
    .UseImage("nginx")
    .Build()
    .Start();
```

---

## New Features

### 1. Multiple Driver Instances

**NEW in v3.0.0**: Register multiple drivers with unique IDs.

```csharp
var kernel = new FluentDockerKernel(autoRegister: false);

// Register multiple Docker instances
kernel.RegisterDriver("local", new DockerCliDriver());
kernel.RegisterDriver("staging", new DockerApiDriver(
    new DockerUri("tcp://staging:2376"),
    stagingCerts
));
kernel.RegisterDriver("prod", new DockerApiDriver(
    new DockerUri("tcp://prod:2376"),
    prodCerts
));

// Deploy to different environments
var localContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("local")
    .UseImage("nginx")
    .Build();

var stagingContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("staging")
    .UseImage("myapp:latest")
    .Build();

var prodContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("prod")
    .UseImage("myapp:v1.0")
    .Build();
```

### 2. SysCtl() Driver Access

**NEW in v3.0.0**: Access drivers directly via SysCtl interface.

```csharp
var kernel = new FluentDockerKernel();
kernel.RegisterDriver("docker", new DockerCliDriver());

// Access container driver
var containerDriver = kernel.SysCtl<IContainerDriver>("docker");
var containers = containerDriver.List(new DriverContext(), all: true);

// Access network driver
var networkDriver = kernel.SysCtl<INetworkDriver>("docker");
var networks = networkDriver.List(new DriverContext());

// Alternative syntax
var containerDriver = kernel.SysCtl("docker", DriverComponent.Container);
```

### 3. Docker + Podman Simultaneously

**NEW in v3.0.0**: Run Docker and Podman containers in the same application.

```csharp
var kernel = new FluentDockerKernel(autoRegister: false);

kernel.RegisterDriver("docker", new DockerCliDriver());
kernel.RegisterDriver("podman", new PodmanCliDriver());

// Docker container
var dockerContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("docker")
    .UseImage("nginx")
    .Build();

// Podman container
var podmanContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("podman")
    .UseImage("nginx")
    .Build();
```

### 4. V3 Async Service Layer

**NEW in v3.0.0**: Complete async service layer using the kernel/driver architecture.

The V3 async service layer (`FluentDocker.Services.V3`) provides full async/await support and uses the driver layer instead of the deprecated Commands namespace.

#### V3 Service Interfaces

| Interface | Description | V2 Equivalent |
|-----------|-------------|---------------|
| `IServiceAsync` | Base async service interface | `IService` |
| `IContainerServiceAsync` | Async container operations | `IContainerService` |
| `IImageServiceAsync` | Async image operations | `IContainerImageService` |
| `INetworkServiceAsync` | Async network operations | `INetworkService` |
| `IVolumeServiceAsync` | Async volume operations | `IVolumeService` |
| `IComposeServiceAsync` | Async compose operations | `ICompositeService` |
| `IHostServiceAsync` | Async host management | `IHostService` |
| `IEngineScopeAsync` | Async engine mode switching | `IEngineScope` |

#### V3 Service Implementations

| Implementation | Uses Driver |
|----------------|-------------|
| `ContainerServiceAsync` | `IContainerDriver` |
| `ImageServiceAsync` | `IImageDriver` |
| `NetworkServiceAsync` | `INetworkDriver` |
| `VolumeServiceAsync` | `IVolumeDriver` |
| `ComposeServiceAsync` | `IComposeDriver` |
| `HostServiceAsync` | `ISystemDriver`, `IContainerDriver`, `IImageDriver`, `INetworkDriver`, `IVolumeDriver` |
| `EngineScopeAsync` | `ISystemDriver` |

#### Example: Using V3 Async Services

```csharp
using FluentDocker.Kernel;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Services.V3;
using FluentDocker.Services.V3.Impl;

// Setup kernel and driver
var kernel = new FluentDockerKernel();
kernel.RegisterDriver("docker", new DockerCliDriver());

// Create host service
var host = new HostServiceAsync(kernel, "docker", "native", isNative: true);

// List containers asynchronously
var containers = await host.GetContainersAsync(all: true);

// Create a container
var container = await host.CreateContainerAsync("nginx:latest", new ContainerCreateOptions
{
    Name = "my-nginx",
    Ports = new Dictionary<string, string> { ["80"] = "8080" },
    StopOnDispose = true,
    DeleteOnDispose = true
});

// Start and use the container
await container.StartAsync();
var info = await container.InspectAsync();
Console.WriteLine($"Container {info.Name} is {info.State.Status}");

// Cleanup
await container.StopAsync();
await container.RemoveAsync();
```

#### Example: Using V3 Engine Scope

```csharp
// Create an engine scope that switches to Linux daemon
await using var scope = await EngineScopeAsync.CreateAsync(kernel, "docker", EngineScopeType.Linux);

// All operations in this scope use Linux daemon
var containers = await host.GetContainersAsync();

// When scope is disposed, original daemon mode is restored
```

#### Example: Using V3 Image Service

```csharp
// Pull an image
var image = await host.PullImageAsync("nginx", "alpine");

// Get image info
var imageInfo = await image.InspectAsync();
Console.WriteLine($"Image size: {imageInfo.Size} bytes");

// Tag the image
await image.TagAsync("myregistry.com/nginx", "v1");

// Push to registry
await image.PushAsync();
```

### 5. Automatic Driver Selection

**NEW in v3.0.0**: Driver selection based on preferences.

```csharp
var kernel = new FluentDockerKernel();
kernel.RegisterDriver("docker-cli", new DockerCliDriver());
kernel.RegisterDriver("docker-api", new DockerApiDriver());

// Prefer API, fallback to CLI
var context = new DriverContext
{
    Preferences = new DriverPreferences
    {
        PreferredDriverIds = new List<string> { "docker-api", "docker-cli" },
        AllowFallback = true
    }
};

// Will use docker-api if available, otherwise docker-cli
var response = kernel.CreateContainer(createParams, context);
```

---

## Migration Scenarios

### Scenario 1: Simple Local Docker

**v2.x.x**:
```csharp
using FluentDocker.Builders;

public class MyApp
{
    public void Run()
    {
        using var container = new Builder()
            .UseContainer()
            .UseImage("postgres:13")
            .WithEnvironment("POSTGRES_PASSWORD=secret")
            .ExposePort(5432)
            .Build()
            .Start();

        // Use container...
    }
}
```

**v3.0.0** (no changes needed):
```csharp
using FluentDocker.Builders;

public class MyApp
{
    public void Run()
    {
        // Exactly the same - uses default kernel
        using var container = new Builder()
            .UseContainer()
            .UseImage("postgres:13")
            .WithEnvironment("POSTGRES_PASSWORD=secret")
            .ExposePort(5432)
            .Build()
            .Start();

        // Use container...
    }
}
```

### Scenario 2: Docker Compose

**v2.x.x**:
```csharp
using var svc = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .Build()
    .Start();

Assert.AreEqual(2, svc.Containers.Count);
```

**v3.0.0** (no changes needed):
```csharp
// Same code works
using var svc = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .Build()
    .Start();

Assert.AreEqual(2, svc.Containers.Count);
```

### Scenario 3: Remote Docker Host

**v2.x.x**:
```csharp
var host = new DockerUri("tcp://remote-docker:2376");
var certs = new CertificatePaths("/path/to/certs");

using var container = new Builder()
    .UseHost()
    .WithHostUri(host)
    .WithCertificates(certs)
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();
```

**v3.0.0**:
```csharp
// Create kernel and register remote driver
var kernel = new FluentDockerKernel(autoRegister: false);
kernel.RegisterDriver("remote", new DockerCliDriver(
    new DockerUri("tcp://remote-docker:2376"),
    new CertificatePaths("/path/to/certs")
));

// Use it
using var container = new Builder(kernel)
    .UseContainer()
    .UseDriver("remote")  // Specify driver
    .UseImage("nginx")
    .Build()
    .Start();
```

### Scenario 4: Testing with Mocks

**v2.x.x**:
```csharp
// Had to mock ProcessExecutor or entire Commands layer
```

**v3.0.0**:
```csharp
// Create mock driver
public class MockDriver : IDriver
{
    public List<string> CreatedContainers = new();

    public IContainerDriver Containers { get; }

    public MockDriver()
    {
        Containers = new MockContainerDriver(this);
    }

    // Implement other IDriver members...
}

// Use mock driver
[Fact]
public void Test_With_Mock()
{
    var kernel = new FluentDockerKernel(autoRegister: false);
    var mock = new MockDriver();
    kernel.RegisterDriver("mock", mock);

    var container = new Builder(kernel)
        .UseContainer()
        .UseDriver("mock")
        .UseImage("test")
        .Build();

    Assert.Contains("test", mock.CreatedContainers);
}
```

### Scenario 5: Multiple Hosts

**v2.x.x** (not supported - had to create multiple Builder instances):
```csharp
// Not easy to manage multiple hosts
```

**v3.0.0**:
```csharp
var kernel = new FluentDockerKernel(autoRegister: false);

// Register multiple Docker hosts
kernel.RegisterDriver("dev", new DockerCliDriver(devHost, devCerts));
kernel.RegisterDriver("staging", new DockerApiDriver(stagingHost, stagingCerts));
kernel.RegisterDriver("prod", new DockerApiDriver(prodHost, prodCerts));

// Deploy to all environments
var devContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("dev")
    .UseImage("myapp:dev")
    .Build()
    .Start();

var stagingContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("staging")
    .UseImage("myapp:rc")
    .Build()
    .Start();

var prodContainer = new Builder(kernel)
    .UseContainer()
    .UseDriver("prod")
    .UseImage("myapp:v1.0")
    .Build()
    .Start();
```

---

## API Changes Reference

### Builder API

| v2.x.x | v3.0.0 | Notes |
|--------|--------|-------|
| `new Builder()` | `new Builder()` | Uses default kernel |
| - | `new Builder(kernel)` | Explicit kernel |
| - | `.UseDriver("id")` | Specify driver ID |

### Service API

| v2.x.x | v3.0.0 | Notes |
|--------|--------|-------|
| `container.DockerHost` | `container.Context.Host` | Moved to context |
| `container.Certificates` | `container.Context.Certificates` | Moved to context |
| - | `container.Kernel` | NEW: Access kernel |
| - | `container.DriverId` | NEW: Which driver |
| - | `container.Context` | NEW: Full context |

### Host Configuration

| v2.x.x | v3.0.0 | Notes |
|--------|--------|-------|
| `.UseHost().WithHostUri()` | Register driver with host | Different approach |
| `.WithCertificates()` | Pass certs to driver constructor | Different approach |

### New APIs

| API | Description |
|-----|-------------|
| `kernel.RegisterDriver(id, driver)` | Register driver with ID |
| `kernel.SysCtl<T>(id)` | Access driver interface |
| `kernel.SysCtl(id, component)` | Access driver component |
| `kernel.GetDriver(id)` | Get entire driver |
| `builder.UseDriver(id)` | Specify which driver to use |

---

## Deprecations

### Removed APIs

These APIs have been removed in v3.0.0:

1. **Builder.UseHost().WithHostUri()** - Use driver registration instead
2. **Builder.WithCertificates()** - Pass to driver constructor
3. **IContainerService.DockerHost** - Use `Context.Host` instead
4. **IContainerService.Certificates** - Use `Context.Certificates` instead

### Compose Command API Changes

All Docker Compose commands now use **struct-based arguments** instead of individual parameters. This provides better extensibility, discoverability, and type safety.

#### Removed Methods (Breaking Change)

The following old-style methods have been **removed**:

| Removed Method | Replacement |
|----------------|-------------|
| `ComposeBuild(host, altProjectName, forceRm, ...)` | `ComposeBuildCommand(host, ComposeBuildCommandArgs)` |
| `ComposeCreate(host, altProjectName, ...)` | `ComposeCreateCommand(host, ComposeCreateCommandArgs)` |
| `ComposeStart(host, altProjectName, ...)` | `ComposeStartCommand(host, ComposeStartCommandArgs)` |
| `ComposeStop(host, altProjectName, timeout, ...)` | `ComposeStopCommand(host, ComposeStopCommandArgs)` |
| `ComposeKill(host, altProjectName, signal, ...)` | `ComposeKillCommand(host, ComposeKillCommandArgs)` |
| `ComposePause(host, altProjectName, ...)` | `ComposePauseCommand(host, ComposePauseCommandArgs)` |
| `ComposeUnPause(host, altProjectName, ...)` | `ComposeUnpauseCommand(host, ComposeUnpauseCommandArgs)` |
| `ComposeScale(host, altProjectName, timeout, ...)` | `ComposeScaleCommand(host, ComposeScaleCommandArgs)` |
| `ComposeVersion(host, altProjectName, ...)` | `ComposeVersionCommand(host, ComposeVersionCommandArgs)` |
| `ComposeRestart(host, altProjectName, ...)` | `ComposeRestartCommand(host, ComposeRestartCommandArgs)` |
| `ComposePort(host, containerId, ...)` | `ComposePortCommand(host, ComposePortCommandArgs)` |
| `ComposeConfig(host, altProjectName, ...)` | `ComposeConfigCommand(host, ComposeConfigCommandArgs)` |
| `ComposeDown(host, altProjectName, ...)` | `ComposeDownCommand(host, ComposeDownCommandArgs)` |
| `ComposeUp(host, altProjectName, ...)` | `ComposeUpCommand(host, ComposeUpCommandArgs)` |
| `ComposeRm(host, altProjectName, ...)` | `ComposeRmCommand(host, ComposeRmCommandArgs)` |
| `ComposePs(host, altProjectName, ...)` | `ComposePsCommand(host, ComposePsCommandArgs)` |
| `ComposePull(host, ...)` | `ComposePullCommand(host, ComposePullCommandArgs)` |

#### New Methods Added

These methods are new in v3.0.0:

| Method | Description |
|--------|-------------|
| `ComposeExecCommand` | Execute command in running service container |
| `ComposeRunCommand` | Run one-off command in service container |
| `ComposeTopCommand` | Display running processes |
| `ComposeImagesCommand` | List images used by containers |
| `ComposeCpCommand` | Copy files between container and host |
| `ComposeLogsCommand` | View logs (non-streaming) |

#### Migration Examples

**v2.x.x**:
```csharp
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
```

**v3.0.0**:
```csharp
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

**v2.x.x**:
```csharp
host.Host.ComposeDown(
    Config.AlternativeServiceName,
    Config.ImageRemoval,
    !Config.KeepVolumes,
    Config.RemoveOrphans,
    Config.EnvironmentNameValue,
    host.Certificates,
    Config.ComposeFilePath.ToArray()
);
```

**v3.0.0**:
```csharp
host.Host.ComposeDownCommand(new ComposeDownCommandArgs
{
    AltProjectName = Config.AlternativeServiceName,
    RemoveImages = Config.ImageRemoval,
    RemoveVolumes = !Config.KeepVolumes,
    RemoveOrphans = Config.RemoveOrphans,
    Env = Config.EnvironmentNameValue,
    Certificates = host.Certificates,
    ComposeFiles = Config.ComposeFilePath.ToArray()
});
```

#### Benefits of the New Pattern

1. **Extensibility**: New options can be added to structs without breaking existing code
2. **Discoverability**: IntelliSense shows all available options in the struct
3. **Type Safety**: Strongly-typed properties prevent parameter ordering mistakes
4. **Readability**: Named properties are clearer than positional parameters
5. **Default Values**: Struct fields have sensible defaults (false for bools, null for references)

### Behavior Changes

1. **Builder()** - Can now optionally accept kernel parameter
2. **Services** - Now require kernel instance in constructor
3. **Commands layer** - May be deprecated in future (use SysCtl instead)

---

## Testing

### Unit Testing with Kernel

**v3.0.0**:
```csharp
[Fact]
public void Test_Container_Creation()
{
    // Create isolated kernel for test
    var kernel = new FluentDockerKernel();

    using var container = new Builder(kernel)
        .UseContainer()
        .UseImage("alpine")
        .Build();

    Assert.NotNull(container);
}

[TearDown]
public void Cleanup()
{
    // Kernel cleanup
    kernel?.Dispose();
}
```

### Integration Testing with Multiple Drivers

```csharp
[Fact]
public void Test_Multiple_Drivers()
{
    var kernel = new FluentDockerKernel(autoRegister: false);
    kernel.RegisterDriver("docker-1", new DockerCliDriver());
    kernel.RegisterDriver("docker-2", new DockerApiDriver());

    var container1 = new Builder(kernel)
        .UseContainer()
        .UseDriver("docker-1")
        .UseImage("nginx")
        .Build();

    var container2 = new Builder(kernel)
        .UseContainer()
        .UseDriver("docker-2")
        .UseImage("nginx")
        .Build();

    Assert.NotEqual(container1.DriverId, container2.DriverId);
}
```

---

## Performance Considerations

### v3.0.0 Improvements

1. **API Drivers**: Can use Docker Engine API for better performance
2. **Driver Caching**: Drivers are cached per kernel instance
3. **Lazy Initialization**: Drivers only initialized when needed

### Recommendations

1. **Reuse Kernel Instances**: Create kernel once, reuse across application
2. **Prefer API Drivers**: Use Docker API driver for better performance
3. **Explicit Driver Selection**: Specify driver ID to avoid selection overhead

---

## Troubleshooting

### Issue: "Builder requires kernel parameter"

**Error**: Constructor expects kernel but none provided.

**Solution**: Either pass kernel or rely on default:
```csharp
// Option 1: Use default kernel
var builder = new Builder();

// Option 2: Pass explicit kernel
var kernel = new FluentDockerKernel();
var builder = new Builder(kernel);
```

### Issue: "Driver 'xxx' not found"

**Error**: Specified driver ID not registered.

**Solution**: Check driver registration:
```csharp
var kernel = new FluentDockerKernel();

// Check available drivers
var driverIds = kernel.GetDriverIds();
Console.WriteLine($"Available: {string.Join(", ", driverIds)}");

// Register missing driver
kernel.RegisterDriver("docker", new DockerCliDriver());
```

### Issue: "DockerHost property not found"

**Error**: `container.DockerHost` doesn't exist.

**Solution**: Use `container.Context.Host` instead:
```csharp
// v2.x.x
var host = container.DockerHost;

// v3.0.0
var host = container.Context.Host;
```

### Issue: "No available drivers"

**Error**: Kernel has no registered drivers.

**Solution**: Register at least one driver:
```csharp
var kernel = new FluentDockerKernel(new FluentDockerKernelOptions
{
    AutoRegisterDrivers = false  // Manual mode
});

// Must register manually
kernel.RegisterDriver("docker", new DockerCliDriver());
```

---

## Summary

### Migration Effort

| Use Case | Effort | Changes Required |
|----------|--------|------------------|
| **All projects** | **Required** | Namespace rename: `Ductus.FluentDocker` → `FluentDocker` |
| Simple local Docker | **Low** | Namespace rename + use default kernel |
| Docker Compose | **Medium** | Namespace rename + update Compose commands to use struct-based args |
| Remote Docker host | **Low-Medium** | Namespace rename + register driver with host/certs |
| Multiple hosts | **Medium** | Namespace rename + register multiple drivers |
| Custom testing | **Low-Medium** | Namespace rename + use mock drivers |

### Migration Checklist

1. ✅ Update NuGet package references (remove `Ductus.` prefix)
2. ✅ Find & replace all `Ductus.FluentDocker` → `FluentDocker` in code
3. ✅ Update logging configuration if filtering by namespace
4. ✅ Review and update any service property accesses (e.g., `DockerHost` → `Context.Host`)
5. ✅ Update all Compose command calls to use new struct-based methods (see Compose API Changes section)

### Benefits of v3.0.0

✅ **Cleaner namespace**: Simplified from `Ductus.FluentDocker` to `FluentDocker`
✅ **Async/await support**: V3 service layer with full async operations
✅ **Multiple runtimes**: Docker and Podman simultaneously
✅ **Multiple hosts**: Manage multiple Docker hosts easily
✅ **Better performance**: API drivers available
✅ **Better testing**: Mock drivers, isolated kernels
✅ **More flexible**: Driver plugins, custom implementations
✅ **Cleaner code**: Explicit kernel management
✅ **Type-safe errors**: `CommandResponse<T>` with error codes instead of exceptions

### Recommended Migration Approach

1. **First**: Do the namespace rename (required for all projects)
2. **Then**: Start with default kernel for simple cases
3. **Later**: Use explicit kernel for better control and testing
4. **As needed**: Register multiple drivers when managing multiple hosts
5. **For advanced use**: Use SysCtl() for direct driver access
6. **For testing**: Create mock drivers for unit testing

The migration path is designed to be **smooth and incremental** - the namespace rename is the only required change, and you can adopt new features as needed.
