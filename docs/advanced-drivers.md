---
layout: default
title: Advanced Drivers
nav_order: 18
---

# Advanced Drivers

The fluent builders (`UseContainer`, `UseCompose`, `UseNetwork`, `UseVolume`,
`UseImage`) cover the common resource graph. Under them each driver exposes extra
ports for Swarm stacks and services, Podman pods, multi-arch manifests, Podman
machines, host inspection, engine switching, event/stats streaming, and system
prune. This page shows how to reach those ports and gives one worked snippet per
capability.

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> it from source — see [Consume the preview](getting-started.md#consume-the-preview). The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## Overview

Every capability here is a driver **port** — a small interface resolved from the
kernel by driver id. You resolve the port with `kernel.SysCtl<T>(driverId)` and
pass a `DriverContext(driverId)` to each call. The result is a
`CommandResponse<T>` with `Success`, `Data`, `Error`, `ErrorCode`, and `ExitCode`.

Not every driver implements every port. Availability matches the capability table
in the [README](https://github.com/mariotoffia/FluentDocker/blob/master/README.md): stacks are Docker CLI, services are Docker CLI and
Docker API, pods/manifests/machines are Podman, and system/streaming ports exist
on all three. Resolving a port a driver does not implement throws
`InterfaceNotSupportedException` (use `kernel.TrySysCtl<T>(id, out var port)` to
probe).

## The driver-port pattern

Resolve once, then call. The rest of the page reuses `kernel`, `system`, and
`context` from here.

```csharp
using FluentDocker.Kernel;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .WithPodmanCli("podman", d => { }) // pods, manifests, and machines below
    .BuildAsync();

// Resolve any port by its interface + the driver id.
var system = kernel.SysCtl<ISystemDriver>("docker");
var context = new DriverContext("docker");
```

`SysCtl<T>` is defined on `FluentDockerKernel` (`FluentDocker/Kernel/FluentDockerKernel.cs`);
`DriverContext` and `CommandResponse<T>` live in `FluentDocker.Model.Drivers`.

## Stack (Swarm)

`IStackDriver` deploys and removes Swarm stacks from compose files. Docker CLI
only.

```csharp
var stacks = kernel.SysCtl<IStackDriver>("docker");

var deploy = await stacks.DeployAsync(context, new StackDeployConfig
{
    StackName = "web",
    ComposeFiles = { "docker-compose.yml" },
    WithRegistryAuth = true
});

Console.WriteLine(deploy.Success ? "stack deployed" : deploy.Error);
```

`IStackDriver` also exposes `ListAsync`, `GetServicesAsync`, `GetTasksAsync`, and
`RemoveAsync` (`FluentDocker/Drivers/IStackDriver.cs`).

## Service (Swarm)

`IServiceDriver` manages individual Swarm services. Docker CLI and Docker API.

```csharp
var services = kernel.SysCtl<IServiceDriver>("docker");

var created = await services.CreateAsync(context, new ServiceCreateConfig
{
    Name = "api",
    Image = "nginx:alpine",
    Replicas = 3
});

var all = await services.ListAsync(context);
```

`CreateAsync`, `UpdateAsync`, `RemoveAsync`, `ListAsync`, and `InspectAsync` are on
`FluentDocker/Drivers/IServiceDriver.cs`.

## Pods

Podman groups containers into pods. `WithinPodmanCli(...).UsePod(...)` builds one
as part of the resource graph; the pod comes back as an `IPodService`.

```csharp
using FluentDocker.Builders;
using FluentDocker.Services;
using System.Linq;

await using var results = await new Builder()
    .WithinPodmanCli("podman", kernel)
    .UsePod(p => p
        .WithName("web-pod")
        .ExposePort("8080"))
    .BuildAsync();

var pod = results.OfType<IPodService>().First();
Console.WriteLine($"pod {pod.Name} ({pod.Id})");
```

`IPodBuilder` (`FluentDocker/Builders/IPodBuilder.cs`) also has `WithPort`,
`WithNetwork`, `WithLabel`, `WithHostname`, and `RemoveOnDispose`. `UsePod` throws
on non-Podman drivers.

## Multi-arch manifests

`IPodmanManifestDriver` builds and pushes manifest lists. Podman only; its types
live in `FluentDocker.Drivers.Podman`.

```csharp
using FluentDocker.Drivers.Podman;

var manifests = kernel.SysCtl<IPodmanManifestDriver>("podman");
var podmanCtx = new DriverContext("podman");

await manifests.CreateAsync(podmanCtx, new ManifestCreateConfig
{
    Name = "registry.example.com/app:1.0",
    Images = { "app:amd64", "app:arm64" }
});

await manifests.PushAsync(podmanCtx, new ManifestPushConfig
{
    ListName = "registry.example.com/app:1.0",
    Destination = "registry.example.com/app:1.0"
});
```

`AddAsync`, `AnnotateAsync`, `InspectAsync`, `RemoveAsync`, and `ExistsAsync`
complete the surface (`FluentDocker/Drivers/Podman/IPodmanManifestDriver.cs`).

## Machine management

`IPodmanMachineDriver` drives the Podman VM (macOS/Windows, and Linux where a
machine is used). Podman only.

```csharp
var machines = kernel.SysCtl<IPodmanMachineDriver>("podman");

await machines.InitAsync(podmanCtx, new MachineInitConfig
{
    Name = "dev",
    Cpus = 4,
    MemoryMiB = 4096,
    Now = true
});

var list = await machines.ListAsync(podmanCtx);
```

`StartAsync`, `StopAsync`, `RemoveAsync`, `InspectAsync`, `SshAsync`, `SetAsync`,
and `InfoAsync` are on `FluentDocker/Drivers/Podman/IPodmanMachineDriver.cs`. See
[Podman production notes](podman.md) for machine behavior per platform.

## Host service and engine scope

`IHostService` is a host-level façade over a driver: system info, disk usage, and
container/image/network/volume listing and creation. Construct it directly.

```csharp
using FluentDocker.Services;
using FluentDocker.Services.Impl;

await using var host = new HostService(kernel, "docker", "local");

var info = await host.GetSystemInfoAsync();
var running = await host.GetRunningContainersAsync();
Console.WriteLine($"{running.Count} running containers");
```

`IEngineScope` switches Docker Desktop between the Linux and Windows daemons and
restores the original on dispose. Create it through `EngineScope.CreateAsync`.

```csharp
using FluentDocker.Services;
using FluentDocker.Services.Impl;

await using var scope = await EngineScope.CreateAsync(
    kernel, "docker", EngineScopeType.Linux);

if (await scope.IsWindowsEngineAsync())
    await scope.UseLinuxAsync();
```

`HostService` and `EngineScope` are in `FluentDocker.Services.Impl`; the
interfaces and `EngineScopeType` are in `FluentDocker.Services`.

## Streaming

`IStreamDriver` streams container events and stats, tails logs, and attaches to a
container's standard streams. All drivers.

```csharp
using System;
using System.Threading;

var stream = kernel.SysCtl<IStreamDriver>("docker");

var events = new StreamEventsConfig();
events.Types.Add("container");

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await foreach (var evt in stream.StreamEventsAsync(context, events, cts.Token))
    Console.WriteLine($"{evt.Timestamp:u} {evt.Type}:{evt.Action}");
```

`StreamStatsAsync(context, containerId, config, ct)` yields `ContainerStats`, and
`AttachAsync(context, containerId, config, ct)` yields an `AttachResult` on `.Data`
with the live streams. The runnable version — start a container, capture its lifecycle
events — is [`Examples/EventDriven`](https://github.com/mariotoffia/FluentDocker/tree/featrure/model-support/Examples/EventDriven).

## System prune

`ISystemDriver.PruneAsync` reclaims space. All drivers. This reuses `system` from
the pattern section above.

```csharp
var prune = await system.PruneAsync(context, new SystemPruneConfig
{
    All = true,
    Volumes = false
});

if (prune.Success)
    Console.WriteLine($"reclaimed {prune.Data.SpaceReclaimed} bytes");
```

`ISystemDriver` also exposes `GetInfoAsync`, `GetVersionAsync`, `GetDiskUsageAsync`,
and the daemon-switch calls (`FluentDocker/Drivers/ISystemDriver.cs`).

## Docker-compatible CLIs (finch, nerdctl)

The Docker CLI driver drives any docker-compatible client, not just `docker`. Pass the
binary name to `WithBinary(...)` on the `WithDockerCli` builder — the driver invokes that
client directly instead of aliasing it to `docker`:

```csharp
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("finch", d => d.WithBinary("finch").AsDefault())
    .BuildAsync();
```

`WithBinary(string binaryName, params string[] searchPaths)` also takes optional directories
to search when the client is not on `PATH`; it sets the `BinaryName` carried on
`DriverContext`, so a per-call `DriverContext { BinaryName = "nerdctl" }` overrides it for one
call. Support is best-effort: some engines differ on a few global flags (for example
`-H` / `--tlsverify`).

## Where to next

- [Driver Extensibility](extensibility.md) — add your own port and driver pack.
- [Architecture](architecture.md) — how the kernel resolves ports.
- [Podman production notes](podman.md) — pods, machines, and manifests in practice.
