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

1. [Learning Path](learning-path.html) for a beginner-to-advanced map
2. [Getting Started](getting-started.html) for your first working container
3. One focused topic: [Containers](containers.html) or [Compose](compose.html)

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

See the [Migration Guide](migration.html) for upgrading from v2.x.

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
| [Learning Path](learning-path.html) | Recommended beginner to advanced journey |
| [Getting Started](getting-started.html) | Installation, prerequisites, first container |
| [Containers](containers.html) | Core lifecycle, ports, env vars, waits |
| [Docker Compose](compose.html) | First multi-service workflow |

### Level 2: Daily Usage

| Topic | Description |
|-------|-------------|
| [Networking](networking.html) | Networks, aliases, static IPs |
| [Volumes](volumes.html) | Persistence and bind mounts |
| [Images](images.html) | Build image workflows |
| [Testing](testing.html) | Testing.Core and adapters |
| [Utilities](utilities.html) | Helpers and extension methods |
| [Error Handling](architecture.html#error-handling) | Exceptions and error codes |

### Level 3: Advanced

| Topic | Description |
|-------|-------------|
| [Architecture](architecture.html) | Kernel/driver internals and async model |
| [Driver Extensibility](extensibility.html) | Driver-aware extension model |
| [Migration](migration.html) | Upgrade from v2.x to v3.x |

## Architecture

FluentDocker uses a three-layer architecture:

```
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
- [Architecture Docs](architecture.html)

## License

Apache 2.0 - See [LICENSE](https://github.com/mariotoffia/FluentDocker/blob/master/LICENSE).
