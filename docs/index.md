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

## New Here?

Start with this sequence:

1. [Learning Path](learning-path.md) for a beginner-to-advanced map
2. [Getting Started](getting-started.md) for your first working container
3. One focused topic: [Containers](containers.md) or [Compose](compose.md)

## What's New in 3.2.0 (in development)

3.2.0 is **in development** and **not yet on NuGet** (latest published: 3.1.0). Build
from source on the feature branch to try it.

- **Docker Model Runner (local LLMs)** — manage and consume local models behind the
  same `Builder → WithinDriver → UseModelRunner()` pattern: chat, streaming chat, and
  embeddings. See [Model Runner](model-runner.md) *(preview)*.

## What's New in v3.0.0

- **Namespace renamed**: `Ductus.FluentDocker` → `FluentDocker`
- **Full async/await support** with CancellationToken
- **Driver Layer architecture** replacing Commands namespace
- **Kernel + WithinDriver() scoping** for multi-driver support
- **Lambda-based builder API** — `UseContainer(Action<IContainerBuilder>)`
- **Container Stats** — CPU, memory, network monitoring
- **Label-based filtering** — 5.5x faster container cleanup
- **Static IPv4/IPv6** assignment for containers
- **Directory copy** support (recursive)
- **Docker Compose V2** — uses `docker compose`

See the [Migration Guide](migration.md) for upgrading from v2.x.

## Quick Start (Beginner)

```csharp
using System.Linq;
using FluentDocker.Builders;
using FluentDocker.Kernel;

// Multiple kernels per app are supported.
using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

### 1) Run one container

```csharp
using FluentDocker.Services.Extensions;

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();

var endpoint = results.Containers.First()
    .ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"Endpoint: {endpoint.Address}:{endpoint.Port}");
```

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

```csharp
using var kernel = await FluentDockerKernel.Create()
    .WithPodmanCli("podman", d => d.WithAutoStartMachine().AsDefault())
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

using var kernel = await FluentDockerKernel.Create()
    .WithPodmanCli("podman", d => d.WithAutoStartMachine().AsDefault())
    .BuildAsync();

var context = new DriverContext("podman");
var kube = kernel.SysCtl<IPodmanKubernetesDriver>("podman");

await kube.PlayAsync(context, new KubePlayConfig { YamlPath = "pod.yaml", Replace = true });
await kube.DownAsync(context, "pod.yaml");
```

`DriverContext` carries per-operation driver state (driver ID, host URI, certs, sudo, timeouts). End-users only construct one when invoking a driver via `SysCtl<T>` directly — the builder/kernel flow supplies it implicitly.

## Installation

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
| [Learning Path](learning-path.md) | Recommended beginner to advanced journey |
| [Getting Started](getting-started.md) | Installation, prerequisites, first container |
| [Containers](containers.md) | Core lifecycle, ports, env vars, waits |
| [Docker Compose](compose.md) | First multi-service workflow |

### Level 2: Daily Usage

| Topic | Description |
|-------|-------------|
| [Networking](networking.md) | Networks, aliases, static IPs |
| [Volumes](volumes.md) | Persistence and bind mounts |
| [Images](images.md) | Build image workflows |
| [Model Runner (LLMs)](model-runner.md) | Manage and consume local LLMs via Docker Model Runner *(preview, v3.2 — not yet released)* |
| [Testing](testing.md) | Testing.Core and adapters |
| [Utilities](utilities.md) | Helpers and extension methods |
| [Error Handling](architecture.md#error-handling) | Exceptions and error codes |

### Level 3: Advanced

| Topic | Description |
|-------|-------------|
| [Architecture](architecture.md) | Kernel/driver internals and async model |
| [Driver Extensibility](extensibility.md) | Driver-aware extension model |
| [Migration](migration.md) | Upgrade from v2.x to v3.x |

## Architecture

FluentDocker uses a three-layer architecture:

```text
┌─────────────────────────────────┐
│         Fluent API              │  Builder pattern
├─────────────────────────────────┤
│       Services Layer            │  Container, Network, Volume
├─────────────────────────────────┤
│        Driver Layer             │  Docker CLI, API, Podman
└─────────────────────────────────┘
```

## Linux Users

Docker requires sudo by default. Configure via the kernel builder:

```csharp
using FluentDocker.Model.Common;

using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d
        .WithSudo(SudoMechanism.NoPassword)
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
