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

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

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

> **Sync vs async:** every builder exposes both `Build()` and `BuildAsync()`. The examples below use `await ...BuildAsync()` throughout — prefer it in async contexts (ASP.NET, UI). Use the synchronous `Build()` in console apps, scripts, or test fixtures.

### Docker Engine API (no CLI required)

```csharp
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerApi("docker-api", d => d.AsDefault())
    .BuildAsync();

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
await using var kernel = await FluentDockerKernel.Create()
    .WithPodmanCli("podman", d => d.AsDefault())
    .BuildAsync();

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

### Docker Model Runner (preview)

> **Preview (v3.2)** — manage local LLMs and run inference (chat, completions, embeddings) through the same fluent builder. Requires [Docker Model Runner](https://docs.docker.com/model-runner/). Preview feature in the upcoming 3.2.0 release; see the docs below.

```csharp
using FluentDocker.Model.Models; // ModelReference

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

await using var runner = await new Builder()
    .WithinDriver("docker", kernel)
    .UseModelRunner()
    .ForModel("ai/smollm2")
    .WithContextSize(4096)        // required on DMR v1.2.1: chat models crash on load without it
    .PullIfMissing()              // pulls the model at build if absent
    .BuildAsync();

// One-shot chat against the default model
var reply = await runner.ChatAsync("Reply with a single word.");

// Streaming, token by token
await foreach (var token in runner.ChatStreamAsync("Count: one two three"))
    Console.Write(token);

// Embeddings — use a dedicated embedding model — a chat model cannot embed.
// Pull the embedding model first, then embed against it.
await runner.PullAsync(ModelReference.Parse("ai/embeddinggemma"));
var vector = await runner.EmbedAsync("hello world", ModelReference.Parse("ai/embeddinggemma"));
```

See the [Docker Model Runner guide](https://github.com/mariotoffia/FluentDocker/blob/master/docs/model-runner.md) for endpoints, configuration, and advanced inference routing.

## Features

- **Multi-driver kernel** — run Docker CLI, Docker API, and Podman side by side
- **Fluent builder** — containers, networks, volumes, compose, pods
- **Wait conditions** — port, HTTP, process, log, health check, custom lambda
- **Async-first** — all operations are async with `CancellationToken` support
- **Auto-cleanup** — resources are disposed when the builder result is disposed
- **Testing integration** — xUnit, NUnit, and MSTest fixtures with full lifecycle management
- **Docker Model Runner** *(preview, v3.2)* — manage local LLMs and run chat, completions, and embeddings via the same builder
- **Security options** — capabilities, read-only root, security-opt, user namespace
- **Cross-platform** — Linux, macOS, Windows; .NET 8 and .NET 10

## Documentation

Full documentation, architecture guides, and advanced examples are available at the [project repository](https://github.com/mariotoffia/FluentDocker).

## License

[Apache 2.0](https://opensource.org/licenses/Apache-2.0)
