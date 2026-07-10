---
layout: default
title: Home
nav_order: 1
description: "FluentDocker - A fluent API for Docker and Docker Compose in .NET"
permalink: /
---

# FluentDocker

| Build | Core | Testing.Xunit | Testing.MsTest | Testing.NUnit |
|:-----:|:----:|:-------------:|:--------------:|:-------------:|
|[![CI](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml/badge.svg)](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml)|[![NuGet](https://img.shields.io/nuget/v/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker)|[![NuGet](https://img.shields.io/nuget/v/FluentDocker.Testing.Xunit.svg)](https://www.nuget.org/packages/FluentDocker.Testing.Xunit)|[![NuGet](https://img.shields.io/nuget/v/FluentDocker.Testing.MsTest.svg)](https://www.nuget.org/packages/FluentDocker.Testing.MsTest)|[![NuGet](https://img.shields.io/nuget/v/FluentDocker.Testing.NUnit.svg)](https://www.nuget.org/packages/FluentDocker.Testing.NUnit)|

FluentDocker is a .NET library providing a fluent API for Docker and Docker Compose. It simplifies container management for development, testing, and CI/CD pipelines.

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> it from source — see [Consume the preview](https://mariotoffia.github.io/FluentDocker/getting-started.html#consume-the-preview). The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## New Here?

First 30 minutes, in order:

1. [Install and verify prerequisites](getting-started.md#installation)
2. [Run your first container](getting-started.md#your-first-container)
3. [Add one wait strategy](getting-started.md#with-wait-strategy)
4. [Review cleanup and exception basics](getting-started.md#exception-handling)

Finish those four steps before opening the architecture or extensibility guides. Then pick
a focused topic — [Containers](containers.md) or [Compose](compose.md) — or follow a
[reading plan by role](#reading-plans-by-role).

## What's New in the 3.2 preview

The 3.2 release line adds preview Docker Model Runner support for local LLM workflows.

- **Docker Model Runner (local LLMs)** — manage and consume local models behind the
  same `Builder → WithinDriver → UseModelRunner()` pattern: chat, streaming chat, and
  embeddings. See [Model Runner](model-runner.md) *(preview)*.

## Release History

See the [CHANGELOG](https://github.com/mariotoffia/FluentDocker/blob/master/CHANGELOG.md) for current release notes and migration-impacting changes.

## Quick Start (Beginner)

### 1) Run one container

```csharp
using System;
using System.Linq;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;

// A kernel is the composition root; multiple kernels per app are supported.
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

await using var results = await new Builder()
    .WithinDockerCli("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();

var endpoint = await results.Containers.First()
    .ToHostExposedEndpointAsync("80/tcp");
Console.WriteLine($"Endpoint: {endpoint.Address}:{endpoint.Port}");
```

> Prefer `await using` + `BuildAsync()`; the synchronous `Build()` wrapper exists only
> for code that cannot be async.

### 2) Run multi-service compose

```csharp
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

## Advanced Quick Samples

### Podman container runtime

See [Podman production notes](podman.md) for machine behavior (macOS/Windows vs Linux),
readiness waits, cancellation, and output caps.

```csharp
await using var kernel = await FluentDockerKernel.Create()
    .WithPodmanCli("podman", d => d.WithAutoStartMachine().AsDefault()) // macOS/Windows only
    .BuildAsync();

await using var results = await new Builder()
    .WithinDriver("podman", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();
```

### Podman Kubernetes (kube play / kube down)

```csharp
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

await using var kernel = await FluentDockerKernel.Create()
    .WithPodmanCli("podman", d => d.WithAutoStartMachine().AsDefault()) // macOS/Windows only
    .BuildAsync();

var context = new DriverContext("podman");
var kube = kernel.SysCtl<IPodmanKubernetesDriver>("podman");

await kube.PlayAsync(context, new KubePlayConfig { YamlPath = "pod.yaml", Replace = true });
await kube.DownAsync(context, "pod.yaml");
```

`DriverContext` carries per-operation driver state (driver ID, host URI, certs, sudo, timeouts). End-users only construct one when invoking a driver via `SysCtl<T>` directly — the builder/kernel flow supplies it implicitly.

## Installation

> **Preview:** the published NuGet is 3.1.0. These docs describe 3.2.0-preview.2 — until it ships, build it from the [`featrure/model-support`](https://github.com/mariotoffia/FluentDocker/tree/featrure/model-support) branch into a local feed ([Consume the preview](getting-started.md#consume-the-preview)); 3.1.0's `WithPort` is container-first (host-first here).

```bash
dotnet add package FluentDocker
dotnet add package FluentDocker.Testing.Xunit   # xUnit adapter
dotnet add package FluentDocker.Testing.MsTest  # MSTest adapter
dotnet add package FluentDocker.Testing.NUnit   # NUnit adapter
```

## Documentation by Level

### Level 1: Start Here

| Topic | Description |
|-------|-------------|
| [Getting Started](getting-started.md) | Installation, prerequisites, first container |
| [Containers](containers.md) | Core lifecycle, ports, env vars, waits |
| [Docker Compose](compose.md) | First multi-service workflow |

### Level 2: Daily Usage

| Topic | Description |
|-------|-------------|
| [Networking](networking.md) | Networks, aliases, static IPs |
| [Volumes](volumes.md) | Persistence and bind mounts |
| [Images](images.md) | Build image workflows |
| [Model Runner (LLMs)](model-runner.md) | Manage and consume local LLMs via Docker Model Runner *(preview, since v3.2)* |
| [Testing](testing.md) | Testing.Core and adapters |
| [Utilities](utilities.md) | Helpers and extension methods |
| [Error Handling](architecture.md#error-handling) | Exceptions and error codes |
| [Troubleshooting](troubleshooting.md) | Common failures, symptoms, and fixes |

### Level 3: Advanced

| Topic | Description |
|-------|-------------|
| [Docker API Driver](docker-api.md) | Binary-free TCP+TLS driver: registry auth, TLS, streams |
| [Podman](podman.md) | Podman runtime: machines, readiness, cancellation, output caps |
| [Advanced Drivers](advanced-drivers.md) | Swarm stacks/services, pods, manifests, machines, streaming, prune |
| [Architecture](architecture.md) | Kernel/driver internals and async model |
| [Service Lifecycle](service-lifecycle.md) | Running state, `StateChange` events, and lifecycle hooks |
| [Driver Extensibility](extensibility.md) | Driver-aware extension model |
| [API Reference](api-reference.md) | Generated type-level reference |
| [Migration](migration.md) | Upgrade from v2.x to v3.x |

## Reading Plans by Role

Pick the plan that matches your goal and read the pages in order.

### Application Developer

1. [Getting Started](getting-started.md)
2. [Containers](containers.md)
3. [Compose](compose.md)
4. [Volumes](volumes.md)
5. [Error Handling](architecture.md#error-handling)

### Test Engineer

1. [Getting Started](getting-started.md)
2. [Testing](testing.md)
3. [Test Categories](testing/test-categories.md)
4. [Compose](compose.md)
5. [Networking](networking.md)

### Platform / Library Engineer

1. [Getting Started](getting-started.md)
2. [Architecture](architecture.md)
3. [Service Lifecycle](service-lifecycle.md)
4. [Driver Extensibility](extensibility.md)
5. [Error Handling](architecture.md#error-handling)
6. [Migration Guide](migration.md)

## Architecture

FluentDocker uses a five-layer architecture:

```text
┌─────────────────────────────────┐
│         Fluent API              │  Builder pattern
├─────────────────────────────────┤
│       Services Layer            │  Container, Network, Volume
├─────────────────────────────────┤
│      Kernel (instantiable)      │  DriverRegistry, SysCtl() driver access
├─────────────────────────────────┤
│        Driver Layer             │  Docker CLI, API, Podman
├─────────────────────────────────┤
│          Model Layer            │  DTOs, enums, value objects
└─────────────────────────────────┘
```

See [Architecture](architecture.md#overview) for the full model with concurrent driver
instances.

## Linux Users

Docker requires sudo by default. Configure via the kernel builder:

```csharp
using FluentDocker.Kernel;
using FluentDocker.Model.Common;

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d
        .WithSudo(SudoMechanism.NoPassword) // SudoMechanism is experimental
        .AsDefault())
    .BuildAsync();
```

Or avoid sudo entirely: `sudo usermod -aG docker $USER`

## Resources

- [GitHub Repository](https://github.com/mariotoffia/FluentDocker)
- [NuGet Package](https://www.nuget.org/packages/FluentDocker)
- [Architecture Docs](architecture.md)

## License

Apache 2.0 - See [LICENSE](https://github.com/mariotoffia/FluentDocker/blob/master/LICENSE).
