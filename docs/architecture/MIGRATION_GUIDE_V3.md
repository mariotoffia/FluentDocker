# Migration Guide: FluentDocker v2.x.x → v3.0.0

## Overview

FluentDocker v3.0.0 introduces a pluggable driver layer architecture with **breaking changes**. This guide helps you migrate from v2.x.x to v3.0.0.

**Key Changes:**
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

### 4. Automatic Driver Selection

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
using Ductus.FluentDocker.Builders;

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
using Ductus.FluentDocker.Builders;

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
| Simple local Docker | **None** | Use default kernel (no changes) |
| Docker Compose | **None** | Use default kernel (no changes) |
| Remote Docker host | **Low** | Register driver with host/certs |
| Multiple hosts | **Medium** | Register multiple drivers |
| Custom testing | **Low-Medium** | Use mock drivers |

### Benefits of v3.0.0

✅ **Multiple runtimes**: Docker and Podman simultaneously
✅ **Multiple hosts**: Manage multiple Docker hosts easily
✅ **Better performance**: API drivers available
✅ **Better testing**: Mock drivers, isolated kernels
✅ **More flexible**: Driver plugins, custom implementations
✅ **Cleaner code**: Explicit kernel management

### Recommended Approach

1. **Start with default kernel** for simple cases (zero changes)
2. **Use explicit kernel** for better control and testing
3. **Register multiple drivers** when managing multiple hosts
4. **Use SysCtl()** for advanced driver access
5. **Create mock drivers** for unit testing

The migration path is designed to be **smooth and incremental** - start with minimal changes and adopt new features as needed.
