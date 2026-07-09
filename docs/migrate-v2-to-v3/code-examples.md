---
layout: default
title: Code Examples
parent: Migration Guide
nav_order: 2
---

# Migration Code Examples

Side-by-side before/after examples for common v2.x.x to v3.0.0 patterns.

{% include preview-banner.html %}

**Key differences to keep in mind:**

- v2 only had Docker CLI. v3 supports multiple drivers (Docker CLI, Docker API, Podman CLI).
- v3 requires a **kernel** with at least one registered driver.
- v3 builder uses **lambdas** for configuration instead of method chaining on a single object.
- v3 `BuildAsync()` auto-starts containers (no separate `.Start()` call).
- v3 `BuildAsync()` returns `BuildResults`, not individual services.
- Extension methods like `ToHostExposedEndpointAsync` require `using FluentDocker.Services.Extensions;`.

---

## 1. Simple Container

### v2 (OLD)

```csharp
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;

using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .ExposePort(80)
    .WaitForPort("80/tcp", 30000)
    .Build()
    .Start();

var endpoint = container.ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"Nginx available at: {endpoint}");
```

### v3 (NEW)

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;
using System;
using System.Linq;

// Step 1: Create a kernel with a Docker CLI driver
// (multiple kernels per app/test session are supported)
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

// Step 2: Build container — BuildAsync() auto-starts, returns BuildResults
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();

var container = results.Containers.First();
var endpoint = await container.ToHostExposedEndpointAsync("80/tcp");
Console.WriteLine($"Nginx available at: {endpoint}");
```

**What changed:**
- `new Builder().UseContainer().UseImage(...)` becomes `new Builder().WithinDriver(...).UseContainer(c => c.UseImage(...))`.
- `ExposePort(80)` (int) becomes `ExposePort("80")` (string).
- `.Build().Start()` becomes `await ...BuildAsync()` (auto-starts).
- The result is `BuildResults`, not a single container. Access via `results.Containers.First()`.

---

## 2. Container with Environment Variables

### v2 (OLD)

```csharp
using Ductus.FluentDocker.Builders;

using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:16-alpine")
    .WithEnvironment("POSTGRES_USER=admin")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    .WithEnvironment("POSTGRES_DB=myapp")
    .ExposePort(5432)
    .WaitForPort("5432/tcp", 30000)
    .Build()
    .Start();
```

### v3 (NEW)

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using System.Linq;

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:16-alpine")
        .WithEnvironment("POSTGRES_USER", "admin")
        .WithEnvironment("POSTGRES_PASSWORD", "secret")
        .WithEnvironment("POSTGRES_DB", "myapp")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000))
    .BuildAsync();

var db = results.Containers.First();
```

**What changed:**
- `WithEnvironment("KEY=VALUE")` is still supported in v3 but you can also use the two-argument overload `WithEnvironment("KEY", "VALUE")` for clarity.
- All configuration sits inside the `UseContainer(c => { ... })` lambda.

---

## 3. Multiple Containers with Network

### v2 (OLD)

```csharp
using Ductus.FluentDocker.Builders;

// v2 required separate Build() calls and manual wiring
using var network = new Builder()
    .UseNetwork("backend-net")
    .Build();

using var redis = new Builder()
    .UseContainer()
    .UseImage("redis:7-alpine")
    .WithName("cache")
    .WithNetwork("backend-net")
    .ExposePort(6379)
    .WaitForPort("6379/tcp", 30000)
    .Build()
    .Start();

using var app = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .WithName("webapp")
    .WithNetwork("backend-net")
    .WithEnvironment("REDIS_HOST=cache")
    .ExposePort(8080)
    .WaitForPort("8080/tcp", 30000)
    .Build()
    .Start();
```

### v3 (NEW)

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using System.Linq;

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

// v3: single builder, multiple operations, one BuildAsync() call
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("backend-net")
        .RemoveOnDispose())
    .UseContainer(c => c
        .UseImage("redis:7-alpine")
        .WithName("cache")
        .WithNetwork("backend-net")
        .ExposePort("6379")
        .WaitForPort("6379/tcp", 30000))
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .WithName("webapp")
        .WithNetwork("backend-net")
        .WithEnvironment("REDIS_HOST", "cache")
        .ExposePort("8080")
        .WaitForPort("8080/tcp", 30000))
    .BuildAsync();

// Access individual services from results
var network = results.Networks.First();
var cache = results.GetContainer("cache");
var webapp = results.GetContainer("webapp");
```

**What changed:**
- Network and containers are declared in a **single builder chain** with one terminal `BuildAsync()`.
- Use `results.GetContainer("name")` to retrieve containers by name.
- `RemoveOnDispose()` on the network builder ensures cleanup.
- `BuildResults` implements `IAsyncDisposable` and disposes all services.

---

## 4. Docker Compose

### v2 (OLD)

```csharp
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;

using var svc = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .RemoveOrphans()
    .WaitForHttpUrl("http://localhost:8000/health")
    .Build()
    .Start();

var containers = svc.Containers;
```

### v3 (NEW)

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using System.Linq;

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans()
        .WithWait()
        .WithWaitTimeout(30))
    .BuildAsync();

var compose = results.ComposeServices.First();
```

**What changed:**
- `.UseContainer().UseCompose()` becomes `.UseCompose(c => { ... })` directly on the builder.
- `FromFile(...)` becomes `WithComposeFile(...)`.
- `RemoveOrphans()` becomes `WithRemoveOrphans()`.
- Wait conditions are compose-level: `WithWait()` and `WithWaitTimeout(seconds)`.
- No `.Start()` needed.

### Compose with Multiple Files and Environment

```csharp
// v3: compose with overrides and environment variables
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFiles("docker-compose.yml", "docker-compose.override.yml")
        .WithProjectName("myproject")
        .WithEnvironment("TAG", "v2.1.0")
        .WithEnvFile(".env.production")
        .WithForceRecreate()
        .WithBuild()
        .WithRemoveOrphans()
        .WithWait()
        .WithWaitTimeout(60))
    .BuildAsync();
```

---

## 5. File Copy Operations

### v2 (OLD)

```csharp
using Ductus.FluentDocker.Extensions;

// Copy file to container
container.CopyTo("/path/on/host/config.json", "/app/config.json");

// Copy file from container
container.CopyFrom("/app/logs/output.log", "/path/on/host/output.log");
```

### v3 (NEW)

```csharp
// Copy file/directory to container (async, from host path)
await container.CopyToAsync("/path/on/host/config.json", "/app/config.json");

// Copy file/directory from container to host path (async)
await container.CopyFromToPathAsync("/app/logs/output.log", "/path/on/host/output.log");

// Copy raw bytes to container
byte[] data = System.Text.Encoding.UTF8.GetBytes("hello world");
await container.CopyToAsync("/app/greeting.txt", data);

// Copy raw bytes from container
byte[] rawContent = await container.CopyFromAsync("/app/config.json");
string json = System.Text.Encoding.UTF8.GetString(rawContent);
```

**What changed:**
- `CopyTo` becomes `CopyToAsync` with two overloads: host-path and byte-array.
- `CopyFrom` becomes `CopyFromToPathAsync` (container path to host path) or `CopyFromAsync` (returns raw bytes).
- All copy operations are async.

---

## 6. Container Stats (New in v3)

This feature is new in v3 -- there is no v2 equivalent.

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using System;
using System.Linq;

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();

var container = results.Containers.First();

// Fetch real-time container stats
var stats = await container.GetStatsAsync();

Console.WriteLine($"CPU: {stats.Cpu.UsagePercent:F2}%");
Console.WriteLine($"Memory: {stats.Memory.Usage / 1024 / 1024} MB / {stats.Memory.Limit / 1024 / 1024} MB");
Console.WriteLine($"Memory: {stats.Memory.UsagePercent:F1}%");
Console.WriteLine($"Network RX: {stats.Network.RxBytes} bytes");
Console.WriteLine($"Network TX: {stats.Network.TxBytes} bytes");
Console.WriteLine($"Disk Read: {stats.Disk.ReadBytes} bytes");
Console.WriteLine($"Disk Write: {stats.Disk.WriteBytes} bytes");
```

**Available stats properties:**

| Property | Type | Description |
|----------|------|-------------|
| `stats.Cpu.UsagePercent` | `double` | CPU usage percentage |
| `stats.Memory.Usage` | `long` | Memory usage in bytes |
| `stats.Memory.Limit` | `long` | Memory limit in bytes |
| `stats.Memory.UsagePercent` | `double` | Memory usage percentage |
| `stats.Network.RxBytes` | `long` | Bytes received |
| `stats.Network.TxBytes` | `long` | Bytes transmitted |
| `stats.Disk.ReadBytes` | `long` | Disk bytes read |
| `stats.Disk.WriteBytes` | `long` | Disk bytes written |

---

## 7. Container Exec

### v2 (OLD)

```csharp
using Ductus.FluentDocker.Extensions;

// Synchronous execute
var result = container.Execute("ls -la /app");
Console.WriteLine(result.StdOut);
```

### v3 (NEW)

```csharp
// Async execute — returns combined stdout
string output = await container.ExecuteAsync("ls -la /app");
Console.WriteLine(output);

// Execute multiple commands
string version = await container.ExecuteAsync("cat /etc/os-release");
string processes = await container.ExecuteAsync("ps aux");
```

**What changed:**
- `Execute(...)` becomes `ExecuteAsync(...)`.
- Returns `string` (stdout) directly instead of a result wrapper.
- All execution is async.

### Using Exec in Builder Lifecycle Hooks

```csharp
// v3: Execute commands automatically on container start/stop
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("ubuntu:22.04")
        .WithCommand("sleep", "infinity")
        .ExecuteOnRunning("mkdir", "-p", "/app/data")
        .ExecuteOnRunning("chmod", "777", "/app/data")
        .ExecuteOnDisposing("rm", "-rf", "/app/data/temp"))
    .BuildAsync();
```

---

## 8. Cleanup / Dispose Pattern

### v2 (OLD)

```csharp
using Ductus.FluentDocker.Builders;

// v2: using disposes the single container
using var container = new Builder()
    .UseContainer()
    .UseImage("redis:7-alpine")
    .ExposePort(6379)
    .WaitForPort("6379/tcp", 30000)
    .Build()
    .Start();

// container is stopped and removed when disposed
```

### v3 (NEW) -- Async Dispose

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using System.Linq;

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

// await using disposes ALL services in the BuildResults
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("redis:7-alpine")
        .ExposePort("6379")
        .WaitForPort("6379/tcp", 30000))
    .BuildAsync();

// All containers, networks, and volumes are cleaned up when results is disposed
```

### v3 (NEW) -- Explicit Async Dispose

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("redis:7-alpine")
        .ExposePort("6379")
        .WaitForPort("6379/tcp", 30000))
    .BuildAsync();

// Or explicit async disposal
var results2 = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:16-alpine")
        .WithEnvironment("POSTGRES_PASSWORD", "test")
        .ExposePort("5432"))
    .BuildAsync();

try
{
    var db = results2.Containers.First();
    // ... use the container ...
}
finally
{
    await results2.DisposeAllAsync();
}
```

**What changed:**
- v2 disposed a single service. v3 `BuildResults` disposes **all** services at once.
- `BuildResults` implements both `IDisposable` (sync) and `IAsyncDisposable` (async).
- Use `await using` for non-blocking cleanup in async code.
- Use `DisposeAllAsync()` for explicit async disposal without `await using`.

### Controlling Dispose Behavior

```csharp
// Keep container alive after dispose (for debugging)
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .KeepRunning())     // Don't stop or remove on dispose
    .BuildAsync();

// Delete volumes on dispose
await using var results2 = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:16-alpine")
        .WithEnvironment("POSTGRES_PASSWORD", "test")
        .WithVolume("/data", "/var/lib/postgresql/data")
        .DeleteVolumeOnDispose()
        .DeleteNamedVolumeOnDispose())
    .BuildAsync();
```

---

## Quick Reference: Common API Renames

| v2 Method | v3 Method |
|-----------|-----------|
| `.UseContainer().UseImage(...)` | `.UseContainer(c => c.UseImage(...))` |
| `.WithEnvironment("K=V")` | `.WithEnvironment("K", "V")` |
| `.ExposePort(80)` | `.ExposePort("80")` |
| `.Mount(host, container, ...)` | `.WithVolume(host, container)` |
| `.WaitForMessageInLogs(msg, ms)` | `.WaitForLogMessage(msg, ms)` |
| `.WaitForHttp(url, ms)` | `.WaitForHttpUrl(url, ms)` or `.WaitForHttp("port/tcp", "/path", ms)` |
| `.Build().Start()` | `await ...BuildAsync()` |
| `container.Execute(...)` | `await container.ExecuteAsync(...)` |
| `container.CopyTo(...)` | `await container.CopyToAsync(...)` |
| `container.CopyFrom(...)` | `await container.CopyFromToPathAsync(...)` |

---

## Namespace Reference

| v2 Namespace | v3 Namespace |
|--------------|--------------|
| `Ductus.FluentDocker.Builders` | `FluentDocker.Builders` |
| `Ductus.FluentDocker.Services` | `FluentDocker.Services` |
| `Ductus.FluentDocker.Services.Extensions` | `FluentDocker.Services.Extensions` |
| `Ductus.FluentDocker.Model.Common` | `FluentDocker.Model.Common` |
| *(n/a)* | `FluentDocker.Kernel` |

---

## See Also

- [API Mapping Reference](api-mapping.md) -- full method-by-method mapping
- [Migration Guide](../migration.md) -- step-by-step migration walkthrough
- [Architecture](../architecture.md) -- v3 kernel and driver architecture
