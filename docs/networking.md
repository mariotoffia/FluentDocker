---
layout: default
title: Networking
nav_order: 5
---

# Networking

FluentDocker provides full support for Docker networks, including custom networks, static IP assignment, and multi-network configurations.

## Kernel Setup

All v3 operations require a kernel instance. Create it once and reuse throughout your application.

```csharp
using FluentDocker.Kernel;
using FluentDocker.Builders;

var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();
```

## Basic Network Creation

### Create a Network

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("my-network")
        .RemoveOnDispose())
    .Build();

var network = results.Networks.First();
Console.WriteLine($"Network: {network.Name}");
```

### Use Network with Container

```csharp
// Create the network
using var netResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("app-network")
        .RemoveOnDispose())
    .Build();

// Create a container on that network (reference by name)
using var containerResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithNetwork("app-network"))
    .Build();

// Container is attached to app-network
```

## Multiple Containers on Same Network

```csharp
// Create the shared network
using var netResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("backend")
        .RemoveOnDispose())
    .Build();

// Database
using var dbResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("db")
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WithNetwork("backend")
        .WaitForPort("5432/tcp", 30000))
    .Build();

// Application (can connect to db by container name)
using var appResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("app")
        .UseImage("myapp:latest")
        .WithEnvironment("DATABASE_HOST=db")
        .WithNetwork("backend")
        .ExposePort("8080"))
    .Build();

// Redis cache
using var cacheResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("cache")
        .UseImage("redis:alpine")
        .WithNetwork("backend"))
    .Build();

// All three containers can communicate by name on "backend"
```

## Network with Subnet

### IPv4 Subnet

```csharp
using var netResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("custom-network")
        .WithSubnet("10.10.0.0/16")
        .WithGateway("10.10.0.1")
        .RemoveOnDispose())
    .Build();

using var containerResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithNetwork("custom-network"))
    .Build();

// Container gets IP from 10.10.0.0/16 range
```

### IPv6 Subnet

```csharp
using var netResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("ipv6-network")
        .WithSubnet("2001:db8::/64")
        .WithGateway("2001:db8::1")
        .WithIPv6()
        .RemoveOnDispose())
    .Build();
```

## Static IP Assignment

### Static IPv4

```csharp
using var netResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("static-ip-net")
        .WithSubnet("10.20.0.0/16")
        .WithGateway("10.20.0.1")
        .RemoveOnDispose())
    .Build();

using var c1Results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("server1")
        .UseImage("nginx:alpine")
        .WithNetwork("static-ip-net")
        .UseIpV4("10.20.0.10"))
    .Build();

using var c2Results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("server2")
        .UseImage("nginx:alpine")
        .WithNetwork("static-ip-net")
        .UseIpV4("10.20.0.11"))
    .Build();

// Containers have predictable IPs
Console.WriteLine("Server1: 10.20.0.10");
Console.WriteLine("Server2: 10.20.0.11");
```

### Static IPv6

```csharp
using var netResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("ipv6-static-net")
        .WithSubnet("2001:db8:1::/64")
        .WithGateway("2001:db8:1::1")
        .WithIPv6()
        .RemoveOnDispose())
    .Build();

using var containerResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithNetwork("ipv6-static-net")
        .UseIpV6("2001:db8:1::100"))
    .Build();
```

### Dual Stack (IPv4 + IPv6)

```csharp
using var netResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("dual-stack-net")
        .WithSubnet("10.30.0.0/16")
        .WithGateway("10.30.0.1")
        .WithIPv6()
        .RemoveOnDispose())
    .Build();

using var containerResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithNetwork("dual-stack-net")
        .UseIpV4("10.30.0.50")
        .UseIpV6("2001:db8:2::50"))
    .Build();
```

## Network Drivers

### Bridge Network (Default)

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("my-bridge")
        .UseDriver("bridge")
        .RemoveOnDispose())
    .Build();
```

### Host Network

```csharp
// Container shares host's network namespace
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithNetworkMode("host"))
    .Build();

// No port mapping needed - uses host ports directly
```

### Overlay Network (Swarm)

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("swarm-overlay")
        .UseDriver("overlay")
        .WithOption("encrypted", "true")
        .RemoveOnDispose())
    .Build();
```

### Macvlan Network

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("macvlan-net")
        .UseDriver("macvlan")
        .WithOption("parent", "eth0")
        .WithSubnet("192.168.1.0/24")
        .WithGateway("192.168.1.1")
        .RemoveOnDispose())
    .Build();
```

## Network Options

### Internal Network

```csharp
// No external connectivity
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("internal-net")
        .AsInternal()
        .RemoveOnDispose())
    .Build();
```

### Network Labels

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("labeled-net")
        .WithLabel("environment", "test")
        .WithLabel("project", "myapp")
        .RemoveOnDispose())
    .Build();
```

## Multi-Network Containers

```csharp
// Frontend network (external access)
using var frontendResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("frontend")
        .WithSubnet("10.40.0.0/24")
        .RemoveOnDispose())
    .Build();

// Backend network (internal only)
using var backendResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("backend")
        .WithSubnet("10.41.0.0/24")
        .AsInternal()
        .RemoveOnDispose())
    .Build();

// API server on frontend network
using var apiResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("api")
        .UseImage("myapi:latest")
        .WithNetwork("frontend")
        .UseIpV4("10.40.0.10")
        .ExposePort("8080"))
    .Build();

// Connect API to backend network as well
var apiContainer = apiResults.Containers.First();
var backendNetwork = backendResults.Networks.First();
await backendNetwork.ConnectAsync(apiContainer.Id);

// Database only on backend network
using var dbResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("db")
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WithNetwork("backend")
        .UseIpV4("10.41.0.20"))
    .Build();

// API can reach both frontend and backend
// DB is only accessible from backend network
```

## Network Inspection

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("inspect-me")
        .WithSubnet("10.50.0.0/16")
        .RemoveOnDispose())
    .Build();

var network = results.Networks.First();
var info = await network.InspectAsync();
Console.WriteLine($"Name: {info.Name}");
Console.WriteLine($"Driver: {info.Driver}");
```

## DNS and Aliases

### Container Aliases

```csharp
using var netResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("aliased-net")
        .RemoveOnDispose())
    .Build();

using var containerResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("myservice")
        .UseImage("nginx:alpine")
        .WithNetworkAlias("aliased-net", "web")
        .WithNetworkAlias("aliased-net", "frontend")
        .WithNetworkAlias("aliased-net", "nginx"))
    .Build();

// Container reachable as: myservice, web, frontend, nginx
```

## Microservices Example

```csharp
// Create isolated network for microservices
using var netResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("microservices")
        .WithSubnet("10.100.0.0/16")
        .RemoveOnDispose())
    .Build();

// API Gateway
using var gatewayResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("gateway")
        .UseImage("kong:latest")
        .WithNetwork("microservices")
        .UseIpV4("10.100.0.10")
        .ExposePort(8000, 8000)
        .ExposePort(8443, 8443))
    .Build();

// User Service
using var userResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("user-service")
        .UseImage("user-service:latest")
        .WithNetwork("microservices")
        .UseIpV4("10.100.1.10"))
    .Build();

// Order Service
using var orderResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("order-service")
        .UseImage("order-service:latest")
        .WithNetwork("microservices")
        .UseIpV4("10.100.2.10"))
    .Build();

// Product Service
using var productResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("product-service")
        .UseImage("product-service:latest")
        .WithNetwork("microservices")
        .UseIpV4("10.100.3.10"))
    .Build();

// Services communicate via DNS names or static IPs
// Gateway at 10.100.0.10 can route to all services
```

## Network Cleanup

### Auto-cleanup with RemoveOnDispose

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("temp-network")
        .RemoveOnDispose())
    .Build();

// Network removed when results is disposed
```

### Disposing BuildResults

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("manual-network")
        .RemoveOnDispose())
    .Build();

// Use network...

// Dispose all services (networks, containers, etc.)
results.Dispose();

// Or use async disposal
await results.DisposeAllAsync();
```

## Testing with Isolated Networks

```csharp
public class NetworkIsolatedTest : IDisposable
{
    private readonly FluentDockerKernel _kernel;
    private readonly BuildResults _netResults;
    private readonly BuildResults _dbResults;
    private readonly BuildResults _apiResults;

    public NetworkIsolatedTest()
    {
        _kernel = FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .Build();

        // Each test run gets isolated network
        var testId = Guid.NewGuid().ToString("N")[..8];

        _netResults = new Builder()
            .WithinDriver("docker", _kernel)
            .UseNetwork(n => n
                .WithName($"test-{testId}")
                .WithSubnet("10.200.0.0/24")
                .RemoveOnDispose())
            .Build();

        _dbResults = new Builder()
            .WithinDriver("docker", _kernel)
            .UseContainer(c => c
                .WithName($"db-{testId}")
                .UseImage("postgres:15-alpine")
                .WithEnvironment("POSTGRES_PASSWORD=test")
                .WithNetwork($"test-{testId}")
                .UseIpV4("10.200.0.10")
                .WaitForPort("5432/tcp", 30000))
            .Build();

        _apiResults = new Builder()
            .WithinDriver("docker", _kernel)
            .UseContainer(c => c
                .WithName($"api-{testId}")
                .UseImage("myapi:test")
                .WithEnvironment("DB_HOST=10.200.0.10")
                .WithNetwork($"test-{testId}")
                .ExposePort("8080")
                .WaitForPort("8080/tcp", 30000))
            .Build();
    }

    [Fact]
    public void Api_CanConnectToDatabase()
    {
        var api = _apiResults.Containers.First();
        // Test connectivity via the API container
        Assert.NotNull(api);
    }

    public void Dispose()
    {
        _apiResults?.Dispose();
        _dbResults?.Dispose();
        _netResults?.Dispose();
        _kernel?.Dispose();
    }
}
```

## Next Steps

- [Volumes](volumes.html) - Data persistence
- [Containers](containers.html) - Container management
- [Docker Compose](compose.html) - Multi-container orchestration
