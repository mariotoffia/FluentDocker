# FluentDocker

[![CI](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml/badge.svg)](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker)
[![Downloads](https://img.shields.io/nuget/dt/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

Fluent API for managing Docker and Podman containers, images, networks, and volumes in .NET. Supports **Docker CLI**, **Docker Engine API**, **Podman CLI**, and **Docker Compose**. Runs on Linux, macOS, and Windows.

## Installation

```shell
dotnet add package FluentDocker
```

**Testing framework integration** (pick one):

```shell
dotnet add package FluentDocker.Testing.Xunit
dotnet add package FluentDocker.Testing.NUnit
dotnet add package FluentDocker.Testing.MsTest
```

## Quick Start

### Docker CLI

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;

await using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .ExposePort("5432")
        .WithEnvironment("POSTGRES_PASSWORD", "mysecretpassword")
        .WaitForPort("5432/tcp", 30000))
    .BuildAsync();

var container = results.Containers.First();
// Container is running and ready to accept connections on port 5432
```

### Docker Engine API (no CLI required)

```csharp
await using var kernel = FluentDockerKernel.Create()
    .WithDockerApi("docker-api", d => d.AsDefault())
    .Build();

// Same builder API — just a different driver
await using var results = await new Builder()
    .WithinDriver("docker-api", kernel)
    .UseContainer(c => c
        .UseImage("redis:7-alpine")
        .ExposePort("6379")
        .WaitForPort("6379/tcp", 10000))
    .BuildAsync();
```

### Podman

```csharp
await using var kernel = FluentDockerKernel.Create()
    .WithPodmanCli("podman", d => d.AsDefault())
    .Build();

await using var results = await new Builder()
    .WithinDriver("podman", kernel)
    .UseContainer(c => c
        .UseImage("docker.io/library/nginx:alpine")
        .ExposePort("80"))
    .BuildAsync();
```

### Docker Compose

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithProjectName("myapp")
        .WithForceRecreate())
    .BuildAsync();
```

### Networks and Volumes

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("my-net")
        .UseDriver("bridge")
        .WithSubnet("172.28.0.0/16")
        .RemoveOnDispose())
    .UseVolume(v => v
        .WithName("my-data")
        .UseDriver("local")
        .RemoveOnDispose())
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithNetwork("my-net")
        .WithVolume("my-data:/var/lib/postgresql/data"))
    .BuildAsync();
```

## Features

- **Multi-driver kernel** — run Docker CLI, Docker API, and Podman side by side
- **Fluent builder** — containers, networks, volumes, compose, pods
- **Wait conditions** — port, HTTP, process, log, health check, custom lambda
- **Async-first** — all operations are async with `CancellationToken` support
- **Auto-cleanup** — resources are disposed when the builder result is disposed
- **Testing integration** — xUnit, NUnit, and MSTest fixtures with full lifecycle management
- **Security options** — capabilities, read-only root, security-opt, user namespace
- **Cross-platform** — Linux, macOS, Windows; .NET 8 and .NET 10

## Documentation

Full documentation, architecture guides, and advanced examples are available at the [project repository](https://github.com/mariotoffia/FluentDocker).

## License

[Apache 2.0](https://opensource.org/licenses/Apache-2.0)
