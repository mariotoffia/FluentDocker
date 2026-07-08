---
layout: default
title: Getting Started
nav_order: 3
---

# Getting Started

This guide helps you install FluentDocker and run your first container.
For the complete beginner-to-advanced map and reading plans by role, see the
[documentation index](index.md#documentation-by-level).

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> from [`master`](https://github.com/mariotoffia/FluentDocker) to use it. The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## Read This Guide in Order

- Step 1: Installation and prerequisites
- Step 2: Basic container example
- Step 3: Add one wait strategy
- Step 4: Optional next steps (named container, compose, multiple containers)

If you are new to FluentDocker, complete Step 1-3 before jumping to later sections.

## Installation

### NuGet Packages

```bash
# Once 3.2.0-preview.2 is published to NuGet (see the note above), install with --prerelease:
dotnet add package FluentDocker --prerelease

# Optional: Test framework adapters
dotnet add package FluentDocker.Testing.Xunit --prerelease   # xUnit adapter
dotnet add package FluentDocker.Testing.MsTest --prerelease  # MSTest adapter
dotnet add package FluentDocker.Testing.NUnit --prerelease   # NUnit adapter
```

### Package References

```xml
<!-- 3.2.0-preview.2 is not on NuGet yet — build from master until it ships. -->
<PackageReference Include="FluentDocker" Version="3.2.0-preview.2" />
<PackageReference Include="FluentDocker.Testing.Xunit" Version="3.2.0-preview.2" />
<PackageReference Include="FluentDocker.Testing.MsTest" Version="3.2.0-preview.2" />
<PackageReference Include="FluentDocker.Testing.NUnit" Version="3.2.0-preview.2" />
```

## Prerequisites

- **Docker** must be installed and running
- **.NET runtime** — FluentDocker targets **net8.0** and **net10.0**, so you can consume it from either
- **Building this repository** requires the **.NET 10 SDK** (`global.json` pins `10.0.100`) — distinct from the runtime targets above

### Verify Docker

```bash
docker --version
docker info
```

## Your First Container

### Basic Example

The v3 API uses a two-step approach: first create a **kernel** (multiple kernels
per application are supported), then use the **Builder** to define and run containers.

```csharp
using System;
using System.Linq;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;

// Step 1: Create a kernel (multiple kernels per app are supported)
await using var kernel = await FluentDockerKernel.Create()
  .WithDockerCli("docker", d => d.AsDefault())
  .BuildAsync();

// Step 2: Build and start an nginx container
await using var results = await new Builder()
  .WithinDriver("docker", kernel)
  .UseContainer(c => c
    .UseImage("nginx:alpine")
    .ExposePort("80"))
  .BuildAsync();

// Get the assigned host port
var container = results.Containers.First();
var endpoint = await container.ToHostExposedEndpointAsync("80/tcp");
Console.WriteLine($"Nginx running at: http://localhost:{endpoint.Port}");

// All containers stop and are removed when results is disposed
```

> A synchronous `Build()` wrapper exists for legacy sync callers, but it blocks on the
> async pipeline. Prefer `await using` + `BuildAsync()` in new code.

### With Wait Strategy

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;

// kernel created as shown above

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=mysecret")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000))
    .BuildAsync();

var container = results.Containers.First();
var endpoint = await container.ToHostExposedEndpointAsync("5432/tcp");
var connectionString =
    $"Host=localhost;Port={endpoint.Port};Database=postgres;Username=postgres;Password=mysecret";
```

### Named Container

```csharp
// kernel created as shown above

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("my-postgres")
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000))
    .BuildAsync();

var container = results.Containers.First();
Console.WriteLine($"Container: {container.Name}");
```

## Driver scopes

Every build runs inside a driver scope. `WithinDriver(id, kernel)` is the generic
form: it takes the driver id as a string, exposes every `Use*` operation, and
validates the driver when the build runs (for example, `UsePod` throws unless the
scope is Podman).

The typed scopes target the same driver but expose only the operations that driver
supports, checked at compile time:

- `WithinDockerCli(id, kernel)` — Docker CLI; adds `UseCompose`, `UseModelRunner`, `UseModel`.
- `WithinDockerApi(id, kernel)` — Docker Engine API; the container/network/volume/image subset.
- `WithinPodmanCli(id, kernel)` — Podman CLI; adds `UsePod`.

Use `WithinDriver` when the id is dynamic or you only need the core operations. Use
a typed scope when you want the driver's extras at the call site — `UseCompose` on
Docker CLI, `UsePod` on Podman CLI. Both forms build the same resources; the README
quick start uses `WithinDockerCli`.

## Linux Users

Docker requires sudo by default. Configure FluentDocker:

Option 1: no sudo (recommended — add your user to the docker group). No code needed;
this is the default.

Option 2: sudo without password:

```csharp
using FluentDocker.Kernel;
using FluentDocker.Model.Common;

await using var kernel = await FluentDockerKernel.Create()
  .WithDockerCli("docker", d => d
    .WithSudo(SudoMechanism.NoPassword) // SudoMechanism is experimental
    .AsDefault())
  .BuildAsync();
```

Option 3: sudo with password:

```csharp
using FluentDocker.Kernel;
using FluentDocker.Model.Common;

await using var kernel = await FluentDockerKernel.Create()
  .WithDockerCli("docker", d => d
    .WithSudo(SudoMechanism.Password, "your-password") // SudoMechanism is experimental
    .AsDefault())
  .BuildAsync();
```

**Best practice**: Add your user to the docker group:
```bash
sudo usermod -aG docker $USER
# Log out and back in for changes to take effect
```

## Async Operations

FluentDocker v3 is async-first. `BuildAsync()` and `await using` are the default:

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;

// kernel created as shown above

// Build containers asynchronously
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("redis:alpine")
        .ExposePort("6379")
        .WaitForPort("6379/tcp", 30000))
    .BuildAsync();

var container = results.Containers.First();
var endpoint = await container.ToHostExposedEndpointAsync("6379/tcp");
Console.WriteLine($"Redis running at: localhost:{endpoint.Port}");
```

## Multiple Containers

The Builder lets you define a network and multiple containers in a single `BuildAsync()` call.
Containers reference the network by its string name.

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;

// kernel created as shown above

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    // Create a network first
    .UseNetwork(n => n
        .WithName("my-network")
        .RemoveOnDispose())
    // Start Redis on the network
    .UseContainer(c => c
        .WithName("my-redis")
        .UseImage("redis:alpine")
        .WithNetwork("my-network")
        .ExposePort("6379")
        .WaitForPort("6379/tcp", 30000))
    // Start app that uses Redis
    .UseContainer(c => c
        .WithName("my-app")
        .UseImage("myapp:latest")
        .WithNetwork("my-network")
        .WithEnvironment("REDIS_HOST=my-redis")
        .ExposePort("8080")
        .WaitForPort("8080/tcp", 30000))
    .BuildAsync();

// Both containers can communicate via the network
var redis = results.GetContainer("my-redis");
var app = results.GetContainer("my-app");
```

## Docker Compose Quick Start

For multi-container applications, use Docker Compose:

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;

// kernel created as shown above

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans())
    .BuildAsync();

// Access compose services
foreach (var compose in results.ComposeServices)
{
    Console.WriteLine($"Compose project: {compose.ProjectName}");
}
```

See [Docker Compose](compose.md) for detailed examples.

## Logging

FluentDocker logs through `Microsoft.Extensions.Logging.Abstractions`. There are
two ways to create a kernel:

- `FluentDockerKernel.Create()` — zero-arg overload that defaults to
  `NullLoggerFactory.Instance`, so nothing is logged. Prefer this for simple
  scenarios where you do not need diagnostics.
- `FluentDockerKernel.Create(loggerFactory)` — pass an explicit `ILoggerFactory`
  when you want structured logs routed to a provider.

**Convention**: use the zero-arg `Create()` for the simple / no-logging case, and
pass a factory only when you want logs.

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using FluentDocker.Kernel;

// No logging — the zero-arg overload defaults to NullLoggerFactory.Instance
await using var quiet = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

// Receive structured logs via any provider
using var factory = LoggerFactory.Create(b => b.AddConsole());
await using var kernel = await FluentDockerKernel.Create(factory)
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

// Suppress all logs explicitly (equivalent to the zero-arg Create())
await using var silent = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

See [Utilities → Logging](utilities.md#logging) for filtering, categories,
and the per-level severity policy.

`AddConsole()` comes from the `Microsoft.Extensions.Logging.Console` package, and
`LoggerFactory.Create` from `Microsoft.Extensions.Logging`. FluentDocker only
references `Microsoft.Extensions.Logging.Abstractions`, so add a provider package
to route logs somewhere: `dotnet add package Microsoft.Extensions.Logging.Console`.

## Exception Handling

Always use try-catch or `using` to ensure cleanup:

```csharp
// kernel created as shown above

try
{
    await using var results = await new Builder()
        .WithinDriver("docker", kernel)
        .UseContainer(c => c
            .UseImage("postgres:15-alpine")
            .ExposePort("5432")
            .WaitForPort("5432/tcp", 10000))
        .BuildAsync();

    var container = results.Containers.First();
    // Use container...
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    // Results and all containers are disposed even on exception
}
```

## Local LLMs (Model Runner)

FluentDocker can also manage and consume **local LLMs** through Docker Model Runner.
Enable it in Docker Desktop (*Settings → AI → Enable Docker Model Runner*, with
host-side TCP on), then follow the canonical [Model Runner guide](model-runner.md).
The inference DTOs are preview and subject to change.

For portable / driver-agnostic code that must degrade gracefully on drivers without
model support, use `TryUseModelRunner(out var runnerBuilder)` instead — it returns
`false` (and a null builder) rather than throwing:

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;

// kernel created as shown above

var scoped = new Builder().WithinDriver("docker", kernel);
if (scoped.TryUseModelRunner(out var runnerBuilder))
{
  await using var runner = await runnerBuilder
    .ForModel("ai/smollm2")
    .PullIfMissing()
    .BuildAsync();
  // Use runner.
}
```

**Driver support**: Model Runner currently supports the **Docker CLI** driver only.
Docker API driver model support is not yet available. **Podman is not supported** — calling `UseModelRunner()` on a
Podman scope throws `InterfaceNotSupportedException`. Use `TryUseModelRunner(out ...)`
when you need to handle unsupported drivers gracefully.

**Chat vs. embedding models**: chat / completion calls (`ChatAsync`,
`ChatStreamAsync`) require a **chat** model such as `ai/smollm2`, while embeddings
(`EmbedAsync`) require an **embedding** model such as `ai/embeddinggemma`. A chat
model cannot produce embeddings and vice versa.

Inference runs over the OpenAI-compatible HTTP API on `:12434`; management uses the
`docker model` CLI — but you only ever code against `IModelRunner`. A model can also
be a managed `IModelService` (loads on start, unloads on dispose) or be wired into a
container with `WithModel(...)`. See the full guide for details.

## Next Steps

- [Containers](containers.md) - Container lifecycle, configuration, and operations
- [Docker Compose](compose.md) - Multi-container orchestration
- [Networking](networking.md) - Custom networks and static IPs
- [Volumes](volumes.md) - Data persistence
- [Images](images.md) - Building custom images
- [Model Runner (local LLMs)](model-runner.md) - Managing & consuming local models
- [Testing](testing.md) - Test fixtures and base classes
- [Troubleshooting](troubleshooting.md) - Common failures and fixes
