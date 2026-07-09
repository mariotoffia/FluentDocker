---
layout: default
title: Networking
nav_order: 6
---

# Networking

FluentDocker provides full support for Docker networks, including custom networks, static IP assignment, and multi-network configurations.

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> from [`featrure/model-support`](https://github.com/mariotoffia/FluentDocker/tree/featrure/model-support) to use it. The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## Step by Step

Basics: [Kernel Setup](#kernel-setup), [Basic Network Creation](#basic-network-creation), [Multiple Containers on Same Network](#multiple-containers-on-same-network); intermediate: [Network with Subnet](#network-with-subnet), [Static IP Assignment](#static-ip-assignment), [DNS and Aliases](#dns-and-aliases), [Network Inspection](#network-inspection); advanced: [Network Drivers](#network-drivers), [Network Options](#network-options), [Multi-Network Containers](#multi-network-containers), [Testing with Isolated Networks](#testing-with-isolated-networks).

## Kernel Setup

All v3 operations require a kernel instance; multiple kernels per application are supported.

```csharp
using FluentDocker.Kernel;
using FluentDocker.Builders;

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

## Basic Network Creation

### Create a Network

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("my-network")
        .RemoveOnDispose())
    .BuildAsync();

var network = results.Networks.First();
Console.WriteLine($"Network: {network.Name}");
```

### Use Network with Container

```csharp
// Create the network
await using var netResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("app-network")
        .RemoveOnDispose())
    .BuildAsync();

// Create a container on that network (reference by name)
await using var containerResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithNetwork("app-network"))
    .BuildAsync();

// Container is attached to app-network
```

## Multiple Containers on Same Network

```csharp
// Create the shared network
await using var netResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("backend")
        .RemoveOnDispose())
    .BuildAsync();

// Database
await using var dbResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("db")
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WithNetwork("backend")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000))
    .BuildAsync();

// Application (can connect to db by container name)
await using var appResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("app")
        .UseImage("myapp:latest")
        .WithEnvironment("DATABASE_HOST=db")
        .WithNetwork("backend")
        .ExposePort("8080"))
    .BuildAsync();

// Redis cache
await using var cacheResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("cache")
        .UseImage("redis:alpine")
        .WithNetwork("backend"))
    .BuildAsync();

// All three containers can communicate by name on "backend"
```

## Network with Subnet

### IPv4 Subnet

```csharp
await using var netResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("custom-network")
        .WithSubnet("10.10.0.0/16")
        .WithGateway("10.10.0.1")
        .RemoveOnDispose())
    .BuildAsync();

await using var containerResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithNetwork("custom-network"))
    .BuildAsync();

// Container gets IP from 10.10.0.0/16 range
```

### Restricting IP Allocation Range

Use `WithIPRange(string ipRange)` on `INetworkBuilder` to set Docker IPAM
`IPRange` / CLI `--ip-range`. This limits which IPs Docker will auto-assign
to containers, keeping the rest of the subnet available for static assignment:

```csharp
await using var netResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("my-net")
        .WithSubnet("10.18.0.0/16")
        .WithIPRange("10.18.1.0/24")
        .WithGateway("10.18.0.1")
        .RemoveOnDispose())
    .BuildAsync();

// Docker will only auto-assign IPs from 10.18.1.0/24
// IPs outside that range (e.g. 10.18.2.x) can be used for static assignment
```

### IPv6 Subnet

```csharp
await using var netResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("ipv6-network")
        .WithSubnet("2001:db8::/64")
        .WithGateway("2001:db8::1")
        .WithIPv6()
        .RemoveOnDispose())
    .BuildAsync();
```

## Static IP Assignment

### Static IPv4

```csharp
await using var netResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("static-ip-net")
        .WithSubnet("10.20.0.0/16")
        .WithGateway("10.20.0.1")
        .RemoveOnDispose())
    .BuildAsync();

await using var c1Results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("server1")
        .UseImage("nginx:alpine")
        .WithNetwork("static-ip-net")
        .WithIPv4("10.20.0.10"))
    .BuildAsync();

await using var c2Results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("server2")
        .UseImage("nginx:alpine")
        .WithNetwork("static-ip-net")
        .WithIPv4("10.20.0.11"))
    .BuildAsync();

// Containers have predictable IPs
Console.WriteLine("Server1: 10.20.0.10");
Console.WriteLine("Server2: 10.20.0.11");
```

### Static IPv6

```csharp
await using var netResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("ipv6-static-net")
        .WithSubnet("2001:db8:1::/64")
        .WithGateway("2001:db8:1::1")
        .WithIPv6()
        .RemoveOnDispose())
    .BuildAsync();

await using var containerResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithNetwork("ipv6-static-net")
        .WithIPv6("2001:db8:1::100"))
    .BuildAsync();
```

### Dual Stack (IPv4 + IPv6)

```csharp
await using var netResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("dual-stack-net")
        .WithSubnet("10.30.0.0/16")
        .WithGateway("10.30.0.1")
        .WithIPv6()
        .RemoveOnDispose())
    .BuildAsync();

await using var containerResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithNetwork("dual-stack-net")
        .WithIPv4("10.30.0.50")
        .WithIPv6("2001:db8:2::50"))
    .BuildAsync();
```

## Network Drivers

### Bridge Network (Default)

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("my-bridge")
        .UseDriver("bridge")
        .RemoveOnDispose())
    .BuildAsync();
```

### Host Network

```csharp
// Container shares host's network namespace
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithNetworkMode("host"))
    .BuildAsync();

// No port mapping needed - uses host ports directly
```

### Overlay Network (Swarm)

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("swarm-overlay")
        .UseDriver("overlay")
        .WithOption("encrypted", "true")
        .RemoveOnDispose())
    .BuildAsync();
```

### Macvlan Network

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("macvlan-net")
        .UseDriver("macvlan")
        .WithOption("parent", "eth0")
        .WithSubnet("192.168.1.0/24")
        .WithGateway("192.168.1.1")
        .RemoveOnDispose())
    .BuildAsync();
```

## Network Options

### Internal Network

```csharp
// No external connectivity
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("internal-net")
        .AsInternal()
        .RemoveOnDispose())
    .BuildAsync();
```

### Network Labels

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("labeled-net")
        .WithLabel("environment", "test")
        .WithLabel("project", "myapp")
        .RemoveOnDispose())
    .BuildAsync();
```

## Multi-Network Containers

```csharp
// Frontend network (external access)
await using var frontendResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("frontend")
        .WithSubnet("10.40.0.0/24")
        .RemoveOnDispose())
    .BuildAsync();

// Backend network (internal only)
await using var backendResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("backend")
        .WithSubnet("10.41.0.0/24")
        .AsInternal()
        .RemoveOnDispose())
    .BuildAsync();

// API server on frontend network (static IP applies to the first configured network)
await using var apiResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("api")
        .UseImage("myapi:latest")
        .WithNetwork("frontend")
        .WithIPv4("10.40.0.10")
        .ExposePort("8080"))
    .BuildAsync();

// Connect API to backend network as well
var apiContainer = apiResults.Containers.First();
var backendNetwork = backendResults.Networks.First();
await backendNetwork.ConnectAsync(apiContainer.Id);

// Database only on backend network
await using var dbResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("db")
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WithNetwork("backend")
        .WithIPv4("10.41.0.20"))
    .BuildAsync();

// API can reach both frontend and backend
// DB is only accessible from backend network
```

## Network Inspection

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("inspect-me")
        .WithSubnet("10.50.0.0/16")
        .RemoveOnDispose())
    .BuildAsync();

var network = results.Networks.First();
var info = await network.InspectAsync();
var connected = await network.GetConnectedContainersAsync();
Console.WriteLine($"Name: {info.Name}");
Console.WriteLine($"Driver: {info.Driver}");
Console.WriteLine($"Connected containers: {string.Join(", ", connected)}");
```

## DNS and Aliases

### Container Aliases

```csharp
await using var netResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("aliased-net")
        .RemoveOnDispose())
    .BuildAsync();

await using var containerResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("myservice")
        .UseImage("nginx:alpine")
        .WithNetworkAlias("aliased-net", "web")
        .WithNetworkAlias("aliased-net", "frontend")
        .WithNetworkAlias("aliased-net", "nginx"))
    .BuildAsync();

// Container reachable as: myservice, web, frontend, nginx. CLI aliases are global; API aliases are per-network.
```

## Microservices Example

```csharp
// Create isolated network for microservices
await using var netResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("microservices")
        .WithSubnet("10.100.0.0/16")
        .RemoveOnDispose())
    .BuildAsync();

// API Gateway
await using var gatewayResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("gateway")
        .UseImage("kong:latest")
        .WithNetwork("microservices")
        .WithIPv4("10.100.0.10")
        .ExposePort(8000, 8000)
        .ExposePort(8443, 8443))
    .BuildAsync();

// User service (add other services with the same WithNetwork/WithIPv4 pattern)
await using var userResults = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("user-service")
        .UseImage("user-service:latest")
        .WithNetwork("microservices")
        .WithIPv4("10.100.1.10"))
    .BuildAsync();

// Services communicate via DNS names or static IPs
// Gateway at 10.100.0.10 can route to all services
```

## Network Cleanup

### Auto-cleanup with RemoveOnDispose

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("temp-network")
        .RemoveOnDispose())
    .BuildAsync();

// Network removed when results is disposed
```

### Disposing BuildResults

Prefer `await using var results = await ...BuildAsync();`; call
`await results.DisposeAllAsync()` only when you cannot scope the result.

## Testing with Isolated Networks

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using Xunit;

public class NetworkIsolatedTest : IAsyncLifetime
{
    private FluentDockerKernel _kernel = null!;
    private BuildResults _netResults = null!;
    private BuildResults _dbResults = null!;
    private BuildResults _apiResults = null!;

    public async ValueTask InitializeAsync()
    {
        _kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        // Each test run gets isolated network
        var testId = Guid.NewGuid().ToString("N")[..8];

        _netResults = await new Builder()
            .WithinDriver("docker", _kernel)
            .UseNetwork(n => n
                .WithName($"test-{testId}")
                .WithSubnet("10.200.0.0/24")
                .RemoveOnDispose())
            .BuildAsync();

        _dbResults = await new Builder()
            .WithinDriver("docker", _kernel)
            .UseContainer(c => c
                .WithName($"db-{testId}")
                .UseImage("postgres:15-alpine")
                .WithEnvironment("POSTGRES_PASSWORD=test")
                .WithNetwork($"test-{testId}")
                .WithIPv4("10.200.0.10")
                .ExposePort("5432")
                .WaitForPort("5432/tcp", 30000))
            .BuildAsync();

        _apiResults = await new Builder()
            .WithinDriver("docker", _kernel)
            .UseContainer(c => c
                .WithName($"api-{testId}")
                .UseImage("myapi:test")
                .WithEnvironment("DB_HOST=10.200.0.10")
                .WithNetwork($"test-{testId}")
                .ExposePort("8080")
                .WaitForPort("8080/tcp", 30000))
            .BuildAsync();
    }

    [Fact]
    public void Api_CanConnectToDatabase()
    {
        var api = _apiResults.Containers.First();
        // Test connectivity via the API container
        Assert.NotNull(api);
    }

    public async ValueTask DisposeAsync()
    {
        await _apiResults.DisposeAsync();
        await _dbResults.DisposeAsync();
        await _netResults.DisposeAsync();
        await _kernel.DisposeAsync();
    }
}
```

## Next Steps

- [Volumes](volumes.md) - Data persistence
- [Containers](containers.md) - Container management
- [Docker Compose](compose.md) - Multi-container orchestration
