---
layout: default
title: Getting Started
nav_order: 3
---

# Getting Started

This guide helps you install FluentDocker and run your first container.
For the complete beginner-to-advanced map, see [Learning Path](learning-path.html).

## Read This Guide in Order

- Step 1: Installation and prerequisites
- Step 2: Basic container example
- Step 3: Add one wait strategy
- Step 4: Optional next steps (named container, compose, async, multiple containers)

If you are new to FluentDocker, complete Step 1-3 before jumping to later sections.

## Installation

### NuGet Packages

```bash
# Core package (includes Testing.Core)
dotnet add package FluentDocker

# Optional: Test framework adapters
dotnet add package FluentDocker.Testing.Xunit   # xUnit adapter
dotnet add package FluentDocker.Testing.MsTest  # MSTest adapter
dotnet add package FluentDocker.Testing.NUnit   # NUnit adapter
```

### Package References

```xml
<PackageReference Include="FluentDocker" Version="3.*" />
<PackageReference Include="FluentDocker.Testing.Xunit" Version="3.*" />
<PackageReference Include="FluentDocker.Testing.MsTest" Version="3.*" />
<PackageReference Include="FluentDocker.Testing.NUnit" Version="3.*" />
```

## Prerequisites

- **Docker** must be installed and running
- **.NET 10.0** or later

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
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;

// Step 1: Create a kernel (multiple kernels per app are supported)
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();

// Step 2: Build and start an nginx container
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80"))
    .Build();

// Get the assigned host port
var container = results.Containers.First();
var endpoint = container.ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"Nginx running at: http://localhost:{endpoint.Port}");

// All containers stop and are removed when results is disposed
```

### With Wait Strategy

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;

// kernel created as shown above

using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=mysecret")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000))
    .Build();

var container = results.Containers.First();
var endpoint = container.ToHostExposedEndpoint("5432/tcp");
var connectionString =
    $"Host=localhost;Port={endpoint.Port};Database=postgres;Username=postgres;Password=mysecret";
```

### Named Container

```csharp
// kernel created as shown above

using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("my-postgres")
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000))
    .Build();

var container = results.Containers.First();
Console.WriteLine($"Container: {container.Name}");
```

## Linux Users

Docker requires sudo by default. Configure FluentDocker:

```csharp
// Option 1: No sudo (recommended - add user to docker group)
// No configuration needed — this is the default

// Option 2: Sudo without password
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.WithSudo(SudoMechanism.NoPassword).AsDefault())
    .Build();

// Option 3: Sudo with password
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.WithSudo(SudoMechanism.Password, "your-password").AsDefault())
    .Build();
```

**Best practice**: Add your user to the docker group:
```bash
sudo usermod -aG docker $USER
# Log out and back in for changes to take effect
```

## Async Operations

FluentDocker v3 supports full async/await via `BuildAsync()`:

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;

// kernel created as shown above

// Build containers asynchronously
using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("redis:alpine")
        .ExposePort("6379")
        .WaitForPort("6379/tcp", 30000))
    .BuildAsync();

var container = results.Containers.First();
var endpoint = container.ToHostExposedEndpoint("6379/tcp");
Console.WriteLine($"Redis running at: localhost:{endpoint.Port}");
```

## Multiple Containers

The Builder lets you define a network and multiple containers in a single `Build()` call.
Containers reference the network by its string name.

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;

// kernel created as shown above

using var results = new Builder()
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
    .Build();

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

using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans())
    .Build();

// Access compose services
foreach (var compose in results.ComposeServices)
{
    Console.WriteLine($"Compose project: {compose.Name}");
}
```

See [Docker Compose](compose.html) for detailed examples.

## Logging

Enable debug logging to troubleshoot issues:

```csharp
// Enable logging
Logging.Enabled();

// Or configure in appsettings.json
```

```json
{
  "Logging": {
    "LogLevel": {
      "FluentDocker": "Debug"
    }
  }
}
```

## Exception Handling

Always use try-catch or `using` to ensure cleanup:

```csharp
// kernel created as shown above

try
{
    using var results = new Builder()
        .WithinDriver("docker", kernel)
        .UseContainer(c => c
            .UseImage("postgres:15-alpine")
            .ExposePort("5432")
            .WaitForPort("5432/tcp", 10000))
        .Build();

    var container = results.Containers.First();
    // Use container...
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    // Results and all containers are disposed even on exception
}
```

## Next Steps

- [Containers](containers.html) - Container lifecycle, configuration, and operations
- [Docker Compose](compose.html) - Multi-container orchestration
- [Networking](networking.html) - Custom networks and static IPs
- [Volumes](volumes.html) - Data persistence
- [Images](images.html) - Building custom images
- [Testing](testing.html) - Test fixtures and base classes
