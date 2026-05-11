---
layout: default
title: API Mapping Reference
parent: Migration Guide
nav_order: 1
---

# v2.x.x to v3.0.0 API Mapping

This document provides a comprehensive mapping between the FluentDocker v2.x.x API
and the v3.0.0 API. Use it as a quick-reference when migrating existing code.

---

## 1. Namespace Changes

Every namespace drops the `Ductus.` prefix. Two namespaces are new in v3.

| v2 Namespace | v3 Namespace |
|---|---|
| `Ductus.FluentDocker.Builders` | `FluentDocker.Builders` |
| `Ductus.FluentDocker.Services` | `FluentDocker.Services` |
| `Ductus.FluentDocker.Model.Common` | `FluentDocker.Model.Common` |
| `Ductus.FluentDocker.Model.Containers` | `FluentDocker.Model.Containers` |
| `Ductus.FluentDocker.Model.Compose` | `FluentDocker.Model.Compose` |
| `Ductus.FluentDocker.Model.Networks` | `FluentDocker.Model.Networks` |
| `Ductus.FluentDocker.Model.Volumes` | `FluentDocker.Model.Volumes` |
| `Ductus.FluentDocker.Model.Images` | `FluentDocker.Model.Images` |
| `Ductus.FluentDocker.Extensions` | `FluentDocker.Extensions` |
| `Ductus.FluentDocker.Commands` | **Removed** -- replaced by Driver Layer |
| *(none)* | `FluentDocker.Kernel` -- kernel setup (new) |
| *(none)* | `FluentDocker.Services.Extensions` -- service extension methods (new) |

**Rule of thumb**: find-and-replace `Ductus.FluentDocker` with `FluentDocker`
across all `using` directives, then add the two new namespaces where needed.

---

## 2. Kernel Setup (New in v3)

v2 had no kernel concept; the library discovered the Docker CLI implicitly.
v3 requires you to create a `FluentDockerKernel` before building any resources.

Because v2 only supported Docker CLI, all migrations use `WithDockerCli`:

```csharp
// v3 -- required before any builder usage
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();
```

If you need sudo:

```csharp
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d
        .AsDefault()
        .WithSudo(SudoMechanism.NoPassword))
    .Build();
```

The kernel is passed into the `Builder` via `WithinDriver()` (see next section).

---

## 3. Builder Pattern Changes

v2 used a flat, chained builder API. v3 uses lambda-scoped sub-builders
nested inside `WithinDriver()`.

### Container

```csharp
// v2
var svc = new Builder()
    .UseContainer()
    .UseImage("postgres:alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    .ExposePort(5432, 5432)
    .Build();
svc.Start();

// v3
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .ExposePort(5432, 5432))
    .Build();
// Build() auto-starts; no explicit Start() needed.
var container = results.Containers[0];
```

### Network

```csharp
// v2
var svc = new Builder()
    .UseNetwork("my-net")
    .Build();

// v3
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n.WithName("my-net"))
    .Build();
var network = results.Networks[0];
```

### Volume

```csharp
// v2
var svc = new Builder()
    .UseVolume("my-vol")
    .Build();

// v3
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("my-vol"))
    .Build();
var volume = results.Volumes[0];
```

### Compose

```csharp
// v2
var svc = new Builder()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .Build();

// v3
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c.WithComposeFile("docker-compose.yml"))
    .Build();
var compose = results.ComposeServices[0];
```

### Quick-Reference Table

| v2 | v3 |
|---|---|
| `new Builder().UseContainer().UseImage("x").Build()` | `new Builder().WithinDriver("docker", kernel).UseContainer(c => c.UseImage("x")).Build()` |
| `.Build()` returns service directly | `.Build()` returns `BuildResults` |
| `.Build()` then `.Start()` | `.Build()` auto-starts |
| `.UseContainer().UseImage(...)` chained | `.UseContainer(c => c.UseImage(...))` lambda |
| `.UseNetwork("name")` chained | `.UseNetwork(n => n.WithName("name"))` lambda |
| `.UseVolume("name")` chained | `.UseVolume(v => v.WithName("name"))` lambda |
| `.UseCompose().FromFile("x")` | `.UseCompose(c => c.WithComposeFile("x"))` lambda |

---

## 4. BuildResults (New in v3)

v2 returned a single service from `Build()`. v3 returns a `BuildResults` object
that holds every resource created during the build.

| Member | Type | Description |
|---|---|---|
| `results.Containers` | `IReadOnlyList<IContainerService>` | All containers |
| `results.Networks` | `IReadOnlyList<INetworkService>` | All networks |
| `results.Volumes` | `IReadOnlyList<IVolumeService>` | All volumes |
| `results.ComposeServices` | `IReadOnlyList<IComposeService>` | All compose stacks |
| `results.GetContainer("name")` | `IContainerService` | Lookup by name |
| `results.All` | `IReadOnlyList<IServiceAsync>` | Every service |
| `results.ForDriver("driverId")` | `IReadOnlyList<IServiceAsync>` | Filter by driver |

`BuildResults` implements `IDisposable` / `IAsyncDisposable`, so wrapping it in
a `using` block tears down every resource:

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c.UseImage("redis:alpine"))
    .Build();

// results.Containers[0] is already running
```

---

## 5. Service Method Changes

| v2 Method | v3 Method | Notes |
|---|---|---|
| `container.Start()` | `container.Start()` | Sync still available |
| | `await container.StartAsync()` | Async variant (new) |
| `container.Stop()` | `container.Stop()` | Sync still available |
| | `await container.StopAsync()` | Async variant (new) |
| `container.Pause()` | `container.Pause()` | Sync still available |
| | `await container.PauseAsync()` | Async variant (new) |
| `container.Resume()` | `container.Start()` | No `Resume()`; `Start()` unpauses |
| `container.GetConfiguration()` | `container.GetConfiguration()` | Extension in `Services.Extensions` |
| | `await container.InspectAsync()` | Async variant (new) |
| `container.ToHostExposedEndpoint(port)` | `container.ToHostExposedEndpoint(port)` | Extension in `Services.Extensions` |
| `container.Logs()` | `await container.GetLogsAsync()` | No synchronous variant |
| `container.Execute(cmd)` | `await container.ExecuteAsync(cmd)` | Note: `ExecuteAsync`, **not** `ExecAsync` |
| `host.ComposeUp(...)` | `await composeDriver.UpAsync(ctx, config)` | Struct-based args (see section 9) |

### Important

- `GetConfiguration()` and `ToHostExposedEndpoint()` moved to extension methods.
  Add `using FluentDocker.Services.Extensions;` to resolve them.
- `Resume()` was removed. Call `Start()` to unpause a paused container.
- `Logs()` has no sync wrapper in v3; use `GetLogsAsync()`.

---

## 6. Container Stats (New in v3)

v3 exposes container resource usage through `GetStatsAsync()`:

```csharp
var stats = await container.GetStatsAsync();

// CPU
double cpuPercent = stats.Cpu.UsagePercent;   // NOT stats.CpuPercent

// Memory
long memUsage = stats.Memory.Usage;           // NOT stats.MemoryUsage
long memLimit = stats.Memory.Limit;

// Network I/O
long rxBytes = stats.Network.RxBytes;
long txBytes = stats.Network.TxBytes;

// Disk I/O
long diskRead  = stats.Disk.ReadBytes;
long diskWrite = stats.Disk.WriteBytes;
```

There is no v2 equivalent; this is entirely new functionality.

---

## 7. File Copy Operations

| v2 | v3 | Notes |
|---|---|---|
| `container.CopyTo(hostPath, containerPath)` | `await container.CopyToAsync(hostPath, containerPath)` | Host to container |
| `container.CopyFrom(containerPath, hostPath)` | `await container.CopyFromToPathAsync(containerPath, hostPath)` | Container to host path |
| *(none)* | `await container.CopyFromAsync(containerPath)` | Returns `byte[]` (new) |

All copy operations are async-only in v3.

---

## 8. SudoMechanism

v2 used global static state to configure sudo. v3 moves this into the
per-driver kernel configuration.

| v2 | v3 |
|---|---|
| `SudoMechanism.NoPassword.SetSudo()` | `.WithDockerCli("docker", d => d.WithSudo(SudoMechanism.NoPassword))` |
| `SudoMechanism.Password.SetSudo("pw")` | `.WithDockerCli("docker", d => d.WithSudo(SudoMechanism.Password, "pw"))` |
| Global static state | Per-driver configuration in kernel builder |

The `SudoMechanism` enum values are unchanged: `None`, `NoPassword`, `Password`.

```csharp
// v2
SudoMechanism.NoPassword.SetSudo();
var svc = new Builder().UseContainer().UseImage("x").Build();

// v3
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d
        .AsDefault()
        .WithSudo(SudoMechanism.NoPassword))
    .Build();

var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c.UseImage("x"))
    .Build();
```

---

## 9. Compose Changes

| Aspect | v2 | v3 |
|---|---|---|
| Binary | `docker-compose` (standalone) | `docker compose` (Compose V2 plugin) |
| Argument style | Positional / named args | Struct-based configs |
| Up | `host.ComposeUp(composeFile, ...)` | `await driver.UpAsync(ctx, new ComposeUpConfig { ... })` |
| Down | `host.ComposeDown(composeFile, ...)` | `await driver.DownAsync(ctx, new ComposeDownConfig { ... })` |
| Build | `host.ComposeBuild(composeFile, ...)` | `await driver.BuildAsync(ctx, new ComposeBuildConfig { ... })` |

### Example

```csharp
// v2
var svc = new Builder()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .RemoveOrphans()
    .Build();
svc.Start();

// v3
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .RemoveOrphans())
    .Build();
```

---

## 10. Removed APIs

The following v2 APIs have no v3 equivalent and have been removed entirely.

| Removed API | Reason | Alternative |
|---|---|---|
| `IMachineDriver` / `Hosts().Discover()` | Docker Machine is deprecated | Use Docker Contexts |
| Docker Toolbox support | Docker Toolbox is EOL | Use Docker Desktop |
| `Ductus.FluentDocker.Commands` namespace | Replaced by Driver Layer | Use `IDriverPack` / driver interfaces |
| `docker-compose` standalone binary | Deprecated upstream | v3 uses `docker compose` V2 plugin |

---

## 11. Async Pattern

v3 adds first-class async support with `CancellationToken` throughout.

| v2 | v3 |
|---|---|
| Mostly synchronous | All operations have async variants |
| No `CancellationToken` support | All async methods accept `CancellationToken` |
| `using var svc = builder.Build()` | `using var results = await builder.BuildAsync(ct)` |
| `IDisposable` cleanup | `IAsyncDisposable` preferred (sync `IDisposable` still supported) |

### Sync vs. Async Build

```csharp
// Sync (still works)
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c.UseImage("redis:alpine"))
    .Build();

// Async (preferred)
using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c.UseImage("redis:alpine"))
    .BuildAsync(cancellationToken);
```

### CancellationToken

Every async method accepts an optional `CancellationToken`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

await container.StartAsync(cts.Token);
await container.StopAsync(cts.Token);
var logs = await container.GetLogsAsync(cts.Token);
var stats = await container.GetStatsAsync(cts.Token);
```

---

## Quick Migration Checklist

1. Replace `Ductus.FluentDocker` with `FluentDocker` in all `using` directives.
2. Add `using FluentDocker.Kernel;` and `using FluentDocker.Services.Extensions;`.
3. Create a `FluentDockerKernel` with `WithDockerCli(...)` before any builder use.
4. Wrap builder sub-calls in lambdas: `.UseContainer(c => c.UseImage(...))`.
5. Pass the kernel via `.WithinDriver("docker", kernel)`.
6. Change `Build()` call sites to expect `BuildResults` instead of a single service.
7. Remove `.Start()` calls after `Build()` (auto-started).
8. Replace `Resume()` with `Start()`.
9. Replace `Logs()` with `await GetLogsAsync()`.
10. Replace `Execute(cmd)` with `await ExecuteAsync(cmd)`.
11. Move sudo config from global static into kernel builder.
12. Replace `docker-compose` references with `docker compose` V2.
13. Remove any Docker Machine / Toolbox code paths.
14. Consider switching to `BuildAsync()` / `IAsyncDisposable` for new code.
