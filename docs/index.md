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

## Quick Start

```csharp
using System.Linq;
using FluentDocker.Builders;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

// Multiple kernels per app are supported.
// This kernel registers both Docker CLI and Podman CLI.
using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .WithPodmanCli("podman", d => d.WithAutoStartMachine())
    .BuildAsync();
```

### 1) Standard container (Docker CLI)

```csharp
await using var results = await new Builder()
    .WithinDockerCli("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();

var endpoint = results.Containers.First()
    .ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"Docker endpoint: {endpoint.Address}:{endpoint.Port}");
```

### 2) Standard container (Podman CLI)

```csharp
await using var results = await new Builder()
    .WithinPodmanCli("podman", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();

var endpoint = results.Containers.First()
    .ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"Podman endpoint: {endpoint.Address}:{endpoint.Port}");
```

### 3) Docker Compose (Docker CLI)

```csharp
await using var results = await new Builder()
    .WithinDockerCli("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans()
        .WithWait()
        .WithWaitTimeout(30))
    .BuildAsync();

var compose = results.ComposeServices.First();
```

### 4) Podman Kubernetes (kube play / kube down)

```csharp
var context = new DriverContext("podman");
var kube = kernel.SysCtl<IPodmanKubernetesDriver>("podman");

await kube.PlayAsync(context, new KubePlayConfig
{
    YamlPath = "pod.yaml",
    Replace = true
});

// Teardown
await kube.DownAsync(context, "pod.yaml");
```

## Installation

```bash
dotnet add package FluentDocker
dotnet add package FluentDocker.Testing.Xunit   # xUnit adapter
dotnet add package FluentDocker.Testing.MsTest  # MSTest adapter
dotnet add package FluentDocker.Testing.NUnit   # NUnit adapter
```

## Documentation

| Topic | Description |
|-------|-------------|
| [Getting Started](getting-started.html) | Installation, prerequisites, first container |
| [Containers](containers.html) | Lifecycle, ports, environment, exec, stats |
| [Docker Compose](compose.html) | Multi-container orchestration |
| [Networking](networking.html) | Custom networks, static IPs, drivers |
| [Volumes](volumes.html) | Named volumes, bind mounts, drivers |
| [Images](images.html) | Build from Dockerfile or inline |
| [Testing](testing.html) | Testing.Core resources + framework adapters |
| [Utilities](utilities.html) | TemplateString, Wget, resources |
| [Extensibility](extensibility.html) | Custom driver interfaces, multi-driver patterns |
| [Migration](migration.html) | Upgrading from v2.x to v3.0 |

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

Docker requires sudo by default. Options:

```csharp
SudoMechanism.None.SetSudo();       // Default
SudoMechanism.NoPassword.SetSudo(); // Passwordless sudo
```

Or: `sudo usermod -aG docker $USER`

## Resources

- [GitHub Repository](https://github.com/mariotoffia/FluentDocker)
- [NuGet Package](https://www.nuget.org/packages/FluentDocker)
- [Architecture Docs](architecture/)

## License

Apache 2.0 - See [LICENSE](https://github.com/mariotoffia/FluentDocker/blob/master/LICENSE).
