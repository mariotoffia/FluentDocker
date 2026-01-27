---
layout: default
title: Getting Started
nav_order: 2
---

# Getting Started

This guide helps you install FluentDocker and run your first container.

## Installation

### NuGet Packages

```bash
# Core package
dotnet add package FluentDocker

# Optional: Test support
dotnet add package FluentDocker.MsTest  # For MSTest
dotnet add package FluentDocker.XUnit   # For xUnit
```

### Package References

```xml
<PackageReference Include="FluentDocker" Version="3.*" />
<PackageReference Include="FluentDocker.MsTest" Version="3.*" />
<PackageReference Include="FluentDocker.XUnit" Version="3.*" />
```

## Prerequisites

- **Docker** must be installed and running
- **.NET 8.0** or later (net8.0, net10.0 supported)

### Verify Docker

```bash
docker --version
docker info
```

## Your First Container

### Basic Example

```csharp
using FluentDocker.Builders;

// Start an nginx container
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .ExposePort(80)
    .Build()
    .Start();

// Get the assigned port
var endpoint = container.ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"Nginx running at: http://localhost:{endpoint.Port}");

// Container stops and removes when disposed
```

### With Wait Strategy

```csharp
using FluentDocker.Builders;

// Start PostgreSQL and wait for it to be ready
using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=mysecret")
    .ExposePort(5432)
    .WaitForPort("5432/tcp", 30000)  // Wait up to 30 seconds
    .Build()
    .Start();

var endpoint = container.ToHostExposedEndpoint("5432/tcp");
var connectionString = $"Host=localhost;Port={endpoint.Port};Database=postgres;Username=postgres;Password=mysecret";
```

### Named Container

```csharp
using var container = new Builder()
    .UseContainer()
    .WithName("my-postgres")
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    .ExposePort(5432)
    .WaitForPort("5432/tcp", 30000)
    .Build()
    .Start();

// Container is named "my-postgres"
Console.WriteLine($"Container: {container.Name}");
```

## Linux Users

Docker requires sudo by default. Configure FluentDocker:

```csharp
// Option 1: No sudo (recommended - add user to docker group)
SudoMechanism.None.SetSudo();

// Option 2: Sudo without password
SudoMechanism.NoPassword.SetSudo();

// Option 3: Sudo with password
SudoMechanism.Password.SetSudo("your-password");
```

**Best practice**: Add your user to the docker group:
```bash
sudo usermod -aG docker $USER
# Log out and back in for changes to take effect
```

## Async Operations

FluentDocker v3 supports full async/await:

```csharp
using FluentDocker.Builders;

// Async container operations
using var container = new Builder()
    .UseContainer()
    .UseImage("redis:alpine")
    .ExposePort(6379)
    .WaitForPort("6379/tcp", 30000)
    .Build()
    .Start();

// Get stats asynchronously
var stats = await container.GetStatsAsync();
Console.WriteLine($"CPU: {stats.CpuPercent:F2}%");
Console.WriteLine($"Memory: {stats.MemoryUsage} bytes");

// Execute commands asynchronously
var result = await container.ExecAsync("redis-cli", "PING");
Console.WriteLine($"Redis says: {result}");
```

## Multiple Containers

```csharp
using FluentDocker.Builders;

// Create a network
using var network = new Builder()
    .UseNetwork("my-network")
    .Build();

// Start Redis
using var redis = new Builder()
    .UseContainer()
    .WithName("my-redis")
    .UseImage("redis:alpine")
    .UseNetwork(network)
    .ExposePort(6379)
    .WaitForPort("6379/tcp", 30000)
    .Build()
    .Start();

// Start app that uses Redis
using var app = new Builder()
    .UseContainer()
    .WithName("my-app")
    .UseImage("myapp:latest")
    .UseNetwork(network)
    .WithEnvironment("REDIS_HOST=my-redis")
    .ExposePort(8080)
    .WaitForPort("8080/tcp", 30000)
    .Build()
    .Start();

// Both containers can communicate via network
```

## Docker Compose Quick Start

For multi-container applications, use Docker Compose:

```csharp
using FluentDocker.Builders;

using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .RemoveOrphans()
    .Build()
    .Start();

// Access individual containers
foreach (var container in services.Containers)
{
    Console.WriteLine($"Container: {container.Name}");
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
try
{
    using var container = new Builder()
        .UseContainer()
        .UseImage("postgres:15-alpine")
        .WaitForPort("5432/tcp", 10000)
        .Build()
        .Start();

    // Use container...
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    // Container is disposed even on exception
}
```

## Next Steps

- [Containers](containers.html) - Container lifecycle, configuration, and operations
- [Docker Compose](compose.html) - Multi-container orchestration
- [Networking](networking.html) - Custom networks and static IPs
- [Volumes](volumes.html) - Data persistence
- [Images](images.html) - Building custom images
- [Testing](testing.html) - Test fixtures and base classes
