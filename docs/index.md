---
layout: default
title: Home
nav_order: 1
description: "FluentDocker - A fluent API for Docker and Docker Compose in .NET"
permalink: /
---

# FluentDocker

| Build | Core | MsTest | XUnit |
|:-----:|:----:|:------:|:-----:|
|[![CI](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml/badge.svg)](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml)|[![NuGet](https://img.shields.io/nuget/v/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker)|[![NuGet](https://img.shields.io/nuget/v/FluentDocker.MsTest.svg)](https://www.nuget.org/packages/FluentDocker.MsTest)|[![NuGet](https://img.shields.io/nuget/v/FluentDocker.XUnit.svg)](https://www.nuget.org/packages/FluentDocker.XUnit)|

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
using FluentDocker.Builders;
using FluentDocker.Kernel;

// 1. Create a kernel (once per application)
using var kernel = FluentDockerKernel.Create()
    .WithDriver("docker", d => d.UseDockerCli().AsDefault())
    .Build();

// 2. Start a container and wait for it to be ready
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .ExposePort("5432")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WaitForPort("5432/tcp", 30000))
    .Build();

var container = results.Containers.First();
var endpoint = container.ToHostExposedEndpoint("5432/tcp");
Console.WriteLine($"Connect to: localhost:{endpoint.Port}");
```

## Docker Compose

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans()
        .WithWait()
        .WithWaitTimeout(30))
    .Build();

var compose = results.ComposeServices.First();
```

## Installation

```bash
dotnet add package FluentDocker
dotnet add package FluentDocker.MsTest  # Optional
dotnet add package FluentDocker.XUnit   # Optional
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
| [Testing](testing.html) | MSTest/xUnit fixtures and patterns |
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
