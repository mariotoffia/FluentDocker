---
layout: default
title: Driver Extensibility
nav_order: 13
description: "Custom driver interfaces, driver-aware builder extensions, and multi-driver patterns"
---

# Driver Extensibility

FluentDocker's extensibility model lets drivers expose custom interfaces and builder extensions without kernel changes. This enables driver-specific features (Podman pods, Docker Swarm, etc.) to integrate cleanly with the fluent API.

## Step by Step

This is an advanced guide. Complete [Architecture](architecture.md) before implementing custom extensions.

- Foundation: [Architecture Overview](#architecture-overview), [Interface Resolution](#interface-resolution), [Driver-Aware Builders](#driver-aware-builders)
- Implementation: [Writing Custom Extensions](#writing-custom-extensions), [Real-World Example: Multi-Driver Deployment](#real-world-example-multi-driver-deployment)
- Reference: [Extension Conventions](#extension-conventions), [DriverPackBase Helper](#driverpackbase-helper)

## Architecture Overview

```text
                                 ┌──────────────────────────────┐
                                 │   Extension Methods          │
                                 │   .UsePod("my-pod")          │
                                 │   .UseSwarmMode(replicas: 3) │
                                 └──────────┬───────────────────┘
                                            │ casts to
                                 ┌──────────▼───────────────────┐
                                 │   IDriverScopedBuilder       │
                                 │   .Kernel + .DriverId        │
                                 └──────────┬───────────────────┘
                                            │ calls
                                 ┌──────────▼───────────────────┐
                                 │   TryDriver<T>() /           │
                                 │   RequireDriver<T>()         │
                                 └──────────┬───────────────────┘
                                            │ delegates to
                        ┌───────────────────▼──────────────────┐
                        │  FluentDockerKernel.SysCtl(id, Type) │
                        └───────────────────┬──────────────────┘
                                            │ resolves via
                        ┌───────────────────▼──────────────────┐
                        │   IDriverInterfaceResolver           │
                        │   on DriverPack or Driver            │
                        └──────────────────────────────────────┘
```

**Key principle:** Builders use the common `IContainerBuilder` API by default. Extension methods detect the active driver at configuration time and apply driver-specific behavior only when the driver supports it.

---

## Interface Resolution

### IDriverInterfaceResolver

Any driver or driver pack can implement `IDriverInterfaceResolver` to expose arbitrary interfaces:

```csharp
public interface IDriverInterfaceResolver
{
    bool TryResolve(Type interfaceType, out object implementation);
    IReadOnlyCollection<Type> GetSupportedInterfaces();
}
```

The kernel resolution path depends on what the ID names; pack and driver paths do not fall through to each other.

If the ID names a driver pack:

| Step | Check | Result |
|------|-------|--------|
| 1 | `IDriverInterfaceResolver` on driver pack | Return resolved instance |
| 2 | Driver pack's `SysCtl(driverId, Type)` | Return or throw |

If the ID names a single driver:

| Step | Check | Result |
|------|-------|--------|
| 1 | `IDriverInterfaceResolver` on driver | Return resolved instance |
| 2 | Direct cast (`driver is T`) | Return or throw |

This means any interface registered with the driver pack or driver is discoverable without kernel changes.

### SysCtl Overloads

```csharp
// Type-safe — throws InterfaceNotSupportedException if not found
var driver = kernel.SysCtl<IContainerDriver>("docker");

// Type-based — useful for runtime resolution
object driver = kernel.SysCtl("docker", typeof(IContainerDriver));

// Non-throwing — returns false if interface not supported
if (kernel.TrySysCtl<IPodmanPodDriver>("podman", out var podDriver))
{
    await podDriver.CreatePodAsync(context, new PodCreateConfig { Name = "my-pod" });
}
```

`TrySysCtl<T>()` returns `false` when the interface is not supported, but still throws `DriverNotFoundException` if the driver ID itself is invalid. This distinction is intentional: a missing driver is a configuration error, while an unsupported interface is a feature check.

---

## Driver-Aware Builders

### IDriverScopedBuilder

`Builder` implements `IDriverScopedBuilder` after `WithinDriver(...)`, and all
resource builders (`ContainerBuilder`, `NetworkBuilder`, `VolumeBuilder`,
`ComposeBuilder`, `ImageBuilder`) carry the same scope inside their lambdas:

```csharp
public interface IDriverScopedBuilder
{
    FluentDockerKernel Kernel { get; }
    string DriverId { get; }
}
```

At the top level, this means portable code can probe optional fluent surfaces directly:

```csharp
var scoped = new Builder().WithinDriver("docker", kernel);
if (scoped.TryUseModelRunner(out var runnerBuilder))
{
  await using var runner = await runnerBuilder.ForModel("ai/smollm2").BuildAsync();
}
```

Inside `UseContainer(...)`, `UseNetwork(...)`, etc. lambdas, cast the public builder
interface to `IDriverScopedBuilder` when you need the same capability probes:

```csharp
new Builder().WithinDriver("podman", kernel).UseContainer(container =>
{
  var scoped = (IDriverScopedBuilder)container;
  var podDriver = scoped.TryDriver<IPodmanPodDriver>();
});
```

### RequireDriver and TryDriver

Two extension methods on `IDriverScopedBuilder` simplify driver-specific resolution:

```csharp
// Throws InterfaceNotSupportedException if not available
T RequireDriver<T>(this IDriverScopedBuilder builder) where T : class;

// Returns null if not available
T TryDriver<T>(this IDriverScopedBuilder builder) where T : class;
```

Use `TryDriver<T>()` for optional features with graceful fallback. Use `RequireDriver<T>()` when the feature is mandatory (e.g., your extension only makes sense with a specific driver).

---

## Writing Custom Extensions

### Step 1: Define the Interface

Create a driver-specific interface in the driver's namespace:

```csharp
// FluentDocker/Drivers/Podman/IPodmanPodDriver.cs
public interface IPodmanPodDriver
{
    Task<CommandResponse<PodCreateResult>> CreatePodAsync(
        DriverContext context, PodCreateConfig config,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<Unit>> RemovePodAsync(
        DriverContext context, string name, bool force = false,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<IList<PodInfo>>> ListPodsAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);
}
```

### Step 2: Register in the Driver Pack

In the driver pack's `InitializeAsync`, register the implementation:

```csharp
// DriverPackBase is optional: it implements IDriverInterfaceResolver and provides
// RegisterDriver<T>() plus protected ResolveSysCtl/TryResolveSysCtl helpers.
// IDriverPack (ISysCtl + IDriverInterfaceResolver) adds the pack lifecycle.
public class CustomDriverPack : DriverPackBase, IDriverPack
{
    public DriverType Type => DriverType.Custom;
    public RuntimeType Runtime => RuntimeType.Unknown;

    public async Task InitializeAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
        // Standard interfaces
        RegisterDriver<IContainerDriver>(new PodmanCliContainerDriver(...));
        RegisterDriver<IImageDriver>(new PodmanCliImageDriver(...));

        // Podman-specific interface
        RegisterDriver<IPodmanPodDriver>(new PodmanCliPodDriver(...));

        await Task.CompletedTask;
    }

    // ISysCtl forwards to the base helpers; capabilities/health report this pack.
    public T SysCtl<T>(string driverId) where T : class
        => (T)ResolveSysCtl(driverId, typeof(T));
    public object SysCtl(string driverId, Type interfaceType)
        => ResolveSysCtl(driverId, interfaceType);
    public bool TrySysCtl<T>(string driverId, out T? instance) where T : class
        => TryResolveSysCtl(out instance);

    public Task<DriverCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(new DriverCapabilities { SupportsContainers = true });

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
```

`DriverPackBase` is optional: it implements `IDriverInterfaceResolver` and gives you `RegisterDriver<T>()` plus the protected `ResolveSysCtl` / `TryResolveSysCtl` helpers. Implement `IDriverPack` (which extends `ISysCtl` + `IDriverInterfaceResolver`) for the pack lifecycle — `InitializeAsync`, `GetCapabilitiesAsync`, `IsHealthyAsync` — and forward `SysCtl` to those helpers. Built-in packs such as `PodmanCliDriverPack` implement `IDriverPack` directly against their own driver map instead of deriving `DriverPackBase`.

### Step 3: Write Builder Extensions

Create extension methods in `Drivers/<driver>/BuilderExtensions`:

```csharp
// FluentDocker/Drivers/Podman/BuilderExtensions/PodmanContainerExtensions.cs
public static class PodmanContainerExtensions
{
    /// <summary>
    /// Associates this container with a Podman pod.
    /// Throws if the current driver does not support pods.
    /// </summary>
    public static IContainerBuilder UsePod(
        this IContainerBuilder builder, string podName)
    {
        if (builder is not IDriverScopedBuilder scoped)
            throw new InvalidOperationException("UsePod requires a driver-scoped builder.");

        if (scoped.TryDriver<IPodmanPodDriver>() == null)
            throw new InvalidOperationException("UsePod requires a Podman driver with pod support.");

        return builder.WithPod(podName);
    }
}
```

**Pattern:** Check `builder is IDriverScopedBuilder`, then `TryDriver<T>()`. Always return the builder for chaining after applying the driver-specific behavior. Throw a clear `InvalidOperationException` when the driver doesn't support the feature.

### Step 4: Use It

```csharp
using FluentDocker.Drivers.Podman.BuilderExtensions;

await new Builder()
    .WithinDriver("podman", kernel)
    .UseContainer(c => c
        .UseImage("redis:7-alpine")
        .UsePod("cache-pod")         // Podman-specific
        .ExposePort(6379, 6379)
        .WaitForPort("6379/tcp"))
    .BuildAsync();
```

When run against a Docker driver, `UsePod()` throws because Docker does not support Podman pods.

---

## Real-World Example: Multi-Driver Deployment

This example deploys a web stack on Docker and a cache cluster in a Podman pod, using a single `Builder` with two driver scopes. It demonstrates common builder calls that work on both drivers alongside Podman-specific extensions.

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Drivers.Podman.BuilderExtensions;

// ── Kernel with both drivers ──────────────────────────────────
var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())  // one default per kernel
    .WithPodmanCli("podman", d => { })            // secondary; resolve by id via SysCtl
    .BuildAsync();

// ── Single builder, two driver scopes ─────────────────────────
var deployment = await new Builder()

    // ── Docker scope: standard web stack ──────────────────────
    .WithinDriver("docker", kernel)

    .UseNetwork(n => n
        .WithName("web-net")
        .WithSubnet("172.20.0.0/16"))

    .UseContainer(c => c
        .UseImage("postgres:16-alpine")
        .WithName("db")
        .WithNetwork("web-net")
        .WithEnvironment("POSTGRES_PASSWORD", "secret")
        .ExposePort(5432, 5432)
        .WaitForPort("5432/tcp"))

    .UseContainer(c => c
        .UseImage("myapp:latest")
        .WithName("api")
        .WithNetwork("web-net")
        .WithEnvironment("DB_HOST", "db")
        .ExposePort(8080, 80)
        .WaitForHttp("80/tcp", "/health"))

    // ── Podman scope: cache cluster in a pod ──────────────────
    .WithinDriver("podman")  // reuses same kernel

    .UseContainer(c => c
        .UseImage("redis:7-alpine")
        .WithName("cache-primary")
        .UsePod("cache-pod")         // Podman: shared network namespace
        .ExposePort(6379, 6379)
        .WaitForPort("6379/tcp"))

    .UseContainer(c => c
        .UseImage("redis:7-alpine")
        .WithName("cache-replica")
        .UsePod("cache-pod")         // Same pod as primary
        .WithCommand("redis-server", "--replicaof", "localhost", "6379"))

    .BuildAsync();

// ── Access results by driver scope ────────────────────────────
var dockerServices = deployment.ForDriver("docker");
var podmanServices = deployment.ForDriver("podman");

// ── Cleanup ───────────────────────────────────────────────────
await deployment.DisposeAllAsync();
kernel.Dispose();
```

### What's Happening

| Line | Common or Specific | Notes |
|------|--------------------|-------|
| `UseImage(...)` | Common | Works on any driver |
| `WithName(...)` | Common | Works on any driver |
| `WithNetwork(...)` | Common | Works on any driver |
| `ExposePort(...)` | Common | Works on any driver |
| `WaitForPort(...)` | Common | Works on any driver |
| `WaitForHttp(...)` | Common | Works on any driver |
| `.UsePod("cache-pod")` | **Podman-specific** | Throws on Docker |
| `.WithinDriver("podman")` | Scope switch | Builder chains across drivers |
| `deployment.ForDriver(...)` | Common | Filter results by driver scope |

The common builder calls (`UseImage`, `WithName`, `ExposePort`, `WaitForPort`) work identically across Docker and Podman. The Podman-specific `.UsePod()` extension applies only when the active driver supports `IPodmanPodDriver`; when the same container builder runs under Docker, the call throws a clear `InvalidOperationException`.

---

## Extension Conventions

When writing driver-specific extensions, follow these conventions:

1. **Namespace:** `FluentDocker.Drivers.<Driver>.BuilderExtensions`
2. **Return type:** Always return the builder interface for chaining
3. **Fallback:** Use `TryDriver<T>()`; no-op for optional enhancements, throw when the extension only makes sense for that driver
4. **Naming:** Use verbs that describe the intent (`UsePod`, `EnableSwarmMode`, `WithSecurityProfile`)
5. **Documentation:** Document unsupported-driver behavior in the XML summary

---

## DriverPackBase Helper

For new driver packs, `DriverPackBase` provides a dictionary-backed implementation of `IDriverInterfaceResolver`:

```csharp
public abstract class DriverPackBase : IDriverInterfaceResolver
{
    // Register an interface implementation during initialization
    protected void RegisterDriver<T>(T driver) where T : class;

    // IDriverInterfaceResolver — automatically implemented
    bool TryResolve(Type interfaceType, out object implementation);
    IReadOnlyCollection<Type> GetSupportedInterfaces();

    // Protected helpers for subclass use
    protected object ResolveSysCtl(string driverId, Type interfaceType);
    protected bool TryResolveSysCtl<T>(out T instance) where T : class;
}
```

Register your driver interfaces via `RegisterDriver<T>()` during initialization. Existing driver packs like `DockerCliDriverPack` implement `IDriverInterfaceResolver` directly (they don't inherit `DriverPackBase`), but new packs can use the base class to avoid boilerplate.

---

## Summary

| Concept | Purpose |
|---------|---------|
| `IDriverInterfaceResolver` | Lets drivers expose arbitrary interfaces |
| `TrySysCtl<T>()` | Non-throwing interface check on the kernel |
| `IDriverScopedBuilder` | Gives builder lambdas access to kernel + driver context |
| `RequireDriver<T>()` | Resolves a driver interface (throws if missing) |
| `TryDriver<T>()` | Resolves a driver interface (returns null if missing) |
| `DriverPackBase` | Optional helper for new driver packs |
| Extension methods | Driver-specific fluent API that either no-ops or fails clearly |
