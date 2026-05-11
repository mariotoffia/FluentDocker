---
layout: default
title: Driver Extensibility
nav_order: 14
description: "Custom driver interfaces, driver-aware builder extensions, and multi-driver patterns"
---

# Driver Extensibility

FluentDocker's extensibility model lets drivers expose custom interfaces and builder extensions without kernel changes. This enables driver-specific features (Podman pods, Docker Swarm, etc.) to integrate cleanly with the fluent API.

## Step by Step

This is an advanced guide. Complete [Architecture](architecture.html) before implementing custom extensions.

- Foundation: [Architecture Overview](#architecture-overview), [Interface Resolution](#interface-resolution), [Driver-Aware Builders](#driver-aware-builders)
- Implementation: [Writing Custom Extensions](#writing-custom-extensions), [Real-World Example: Multi-Driver Deployment](#real-world-example-multi-driver-deployment)
- Reference: [Extension Conventions](#extension-conventions), [DriverPackBase Helper](#driverpackbase-helper)

## Architecture Overview

```
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

The kernel uses a cascading resolution strategy when you call `SysCtl(driverId, Type)`:

| Step | Check | Fallback |
|------|-------|----------|
| 1 | `IDriverInterfaceResolver` on driver pack | Continue |
| 2 | Driver pack's `SysCtl(driverId, Type)` | Continue |
| 3 | `IDriverInterfaceResolver` on driver | Continue |
| 4 | Direct cast (`driver is T`) | Throw |

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
    await podDriver.CreatePodAsync(context, "my-pod");
}

// Legacy enum — delegates internally to type-based resolution
var networkDriver = kernel.SysCtl("docker", DriverComponent.Network);
```

> **Note:** `DriverComponent` is `[Obsolete]` and slated for removal in v4 — prefer `SysCtl<T>(driverId)` (or the type-based resolver shown above) for new code.

`TrySysCtl<T>()` returns `false` when the interface is not supported, but still throws `DriverNotFoundException` if the driver ID itself is invalid. This distinction is intentional: a missing driver is a configuration error, while an unsupported interface is a feature check.

---

## Driver-Aware Builders

### IDriverScopedBuilder

All internal builders (`ContainerBuilder`, `NetworkBuilder`, `VolumeBuilder`, `ComposeBuilder`, `ImageBuilder`) implement `IDriverScopedBuilder`:

```csharp
public interface IDriverScopedBuilder
{
    FluentDockerKernel Kernel { get; }
    string DriverId { get; }
}
```

Inside any `UseContainer(...)`, `UseNetwork(...)`, etc. lambda, the builder you receive carries the kernel and driver context from the enclosing `WithinDriver()` scope.

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
        DriverContext context, string name,
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
public class PodmanCliDriverPack : DriverPackBase
{
    protected override async Task OnInitializeAsync(
        DriverContext context, CancellationToken ct)
    {
        // Standard interfaces
        RegisterDriver<IContainerDriver>(new PodmanContainerDriver(...));
        RegisterDriver<IImageDriver>(new PodmanImageDriver(...));

        // Podman-specific interface
        RegisterDriver<IPodmanPodDriver>(new PodmanPodDriver(...));
    }
}
```

`DriverPackBase` provides `RegisterDriver<T>()` backed by a dictionary, which automatically implements `IDriverInterfaceResolver`.

### Step 3: Write Builder Extensions

Create extension methods in `Drivers/<driver>/BuilderExtensions`:

```csharp
// FluentDocker/Drivers/Podman/BuilderExtensions/PodmanContainerExtensions.cs
public static class PodmanContainerExtensions
{
    /// <summary>
    /// Associates this container with a Podman pod.
    /// No-op if the current driver does not support pods.
    /// </summary>
    public static IContainerBuilder UsePod(
        this IContainerBuilder builder, string podName)
    {
        if (builder is IDriverScopedBuilder scoped)
        {
            var podDriver = scoped.TryDriver<IPodmanPodDriver>();
            if (podDriver != null)
            {
                builder.WithLabel("io.podman.pod", podName);
            }
        }
        return builder;
    }
}
```

**Pattern:** Check `builder is IDriverScopedBuilder`, then `TryDriver<T>()`. Always return the builder for chaining. Gracefully no-op when the driver doesn't support the feature.

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

When run against a Docker driver, `UsePod()` simply does nothing and the container is created normally.

---

## Real-World Example: Multi-Driver Deployment

This example deploys a web stack on Docker and a cache cluster in a Podman pod, using a single `Builder` with two driver scopes. It demonstrates common builder calls that work on both drivers alongside Podman-specific extensions.

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Drivers.Podman.BuilderExtensions;

// ── Kernel with both drivers ──────────────────────────────────
var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d
        .AsDefault())
    .WithPodmanCli("podman", d => d
        .AsDefault())
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
| `.UsePod("cache-pod")` | **Podman-specific** | No-ops on Docker |
| `.WithinDriver("podman")` | Scope switch | Builder chains across drivers |
| `deployment.ForDriver(...)` | Common | Filter results by driver scope |

The common builder calls (`UseImage`, `WithName`, `ExposePort`, `WaitForPort`) work identically across Docker and Podman. The Podman-specific `.UsePod()` extension applies only when the active driver supports `IPodmanPodDriver`; when the same container builder runs under Docker, the call is a no-op.

---

## Extension Conventions

When writing driver-specific extensions, follow these conventions:

1. **Namespace:** `FluentDocker.Drivers.<Driver>.BuilderExtensions`
2. **Return type:** Always return the builder interface for chaining
3. **Fallback:** Use `TryDriver<T>()` and no-op when unsupported, unless the extension only makes sense for that driver
4. **Naming:** Use verbs that describe the intent (`UsePod`, `EnableSwarmMode`, `WithSecurityProfile`)
5. **Documentation:** Document no-op behavior in the XML summary

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
| Extension methods | Driver-specific fluent API that gracefully no-ops |
