---
layout: default
title: Networking
nav_order: 5
---

# Networking

FluentDocker provides full support for Docker networks, including custom networks, static IP assignment, and multi-network configurations.

## Basic Network Creation

### Create a Network

```csharp
using FluentDocker.Builders;

using var network = new Builder()
    .UseNetwork("my-network")
    .Build();

// Network is created
Console.WriteLine($"Network: {network.Name}");
```

### Use Network with Container

```csharp
using var network = new Builder()
    .UseNetwork("app-network")
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .UseNetwork(network)
    .Build()
    .Start();

// Container is attached to app-network
```

## Multiple Containers on Same Network

```csharp
using var network = new Builder()
    .UseNetwork("backend")
    .Build();

// Database
using var db = new Builder()
    .UseContainer()
    .WithName("db")
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    .UseNetwork(network)
    .WaitForPort("5432/tcp", 30000)
    .Build()
    .Start();

// Application (can connect to db by name)
using var app = new Builder()
    .UseContainer()
    .WithName("app")
    .UseImage("myapp:latest")
    .WithEnvironment("DATABASE_HOST=db")  // Use container name
    .UseNetwork(network)
    .ExposePort(8080)
    .Build()
    .Start();

// Redis cache
using var cache = new Builder()
    .UseContainer()
    .WithName("cache")
    .UseImage("redis:alpine")
    .UseNetwork(network)
    .Build()
    .Start();

// All three containers can communicate by name
```

## Network with Subnet

### IPv4 Subnet

```csharp
using var network = new Builder()
    .UseNetwork("custom-network")
    .UseSubnet("10.10.0.0/16")
    .UseGateway("10.10.0.1")
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .UseNetwork(network)
    .Build()
    .Start();

// Container gets IP from 10.10.0.0/16 range
```

### IPv6 Subnet

```csharp
using var network = new Builder()
    .UseNetwork("ipv6-network")
    .UseSubnet("2001:db8::/64")
    .UseGateway("2001:db8::1")
    .UseIpV6()  // Enable IPv6
    .Build();
```

## Static IP Assignment

### Static IPv4

```csharp
using var network = new Builder()
    .UseNetwork("static-ip-net")
    .UseSubnet("10.20.0.0/16")
    .UseGateway("10.20.0.1")
    .Build();

using var container1 = new Builder()
    .UseContainer()
    .WithName("server1")
    .UseImage("nginx:alpine")
    .UseNetwork(network)
    .UseIpV4("10.20.0.10")  // Static IP
    .Build()
    .Start();

using var container2 = new Builder()
    .UseContainer()
    .WithName("server2")
    .UseImage("nginx:alpine")
    .UseNetwork(network)
    .UseIpV4("10.20.0.11")  // Static IP
    .Build()
    .Start();

// Containers have predictable IPs
Console.WriteLine("Server1: 10.20.0.10");
Console.WriteLine("Server2: 10.20.0.11");
```

### Static IPv6

```csharp
using var network = new Builder()
    .UseNetwork("ipv6-static-net")
    .UseSubnet("2001:db8:1::/64")
    .UseGateway("2001:db8:1::1")
    .UseIpV6()
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .UseNetwork(network)
    .UseIpV6("2001:db8:1::100")  // Static IPv6
    .Build()
    .Start();
```

### Dual Stack (IPv4 + IPv6)

```csharp
using var network = new Builder()
    .UseNetwork("dual-stack-net")
    .UseSubnet("10.30.0.0/16")
    .UseGateway("10.30.0.1")
    .UseIpV6()
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .UseNetwork(network)
    .UseIpV4("10.30.0.50")
    .UseIpV6("2001:db8:2::50")
    .Build()
    .Start();
```

## Network Drivers

### Bridge Network (Default)

```csharp
using var network = new Builder()
    .UseNetwork("my-bridge")
    .UseDriver("bridge")
    .Build();
```

### Host Network

```csharp
// Container shares host's network namespace
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .UseNetwork("host")  // Special network name
    .Build()
    .Start();

// No port mapping needed - uses host ports directly
```

### Overlay Network (Swarm)

```csharp
using var network = new Builder()
    .UseNetwork("swarm-overlay")
    .UseDriver("overlay")
    .UseDriverOption("encrypted", "true")
    .Build();
```

### Macvlan Network

```csharp
using var network = new Builder()
    .UseNetwork("macvlan-net")
    .UseDriver("macvlan")
    .UseDriverOption("parent", "eth0")
    .UseSubnet("192.168.1.0/24")
    .UseGateway("192.168.1.1")
    .Build();
```

## Network Options

### Internal Network

```csharp
// No external connectivity
using var network = new Builder()
    .UseNetwork("internal-net")
    .UseInternal()  // No outbound access
    .Build();
```

### Network Labels

```csharp
using var network = new Builder()
    .UseNetwork("labeled-net")
    .WithLabel("environment", "test")
    .WithLabel("project", "myapp")
    .Build();
```

## Multi-Network Containers

```csharp
// Frontend network (external access)
using var frontendNet = new Builder()
    .UseNetwork("frontend")
    .UseSubnet("10.40.0.0/24")
    .Build();

// Backend network (internal only)
using var backendNet = new Builder()
    .UseNetwork("backend")
    .UseSubnet("10.41.0.0/24")
    .UseInternal()
    .Build();

// API server connects to both networks
using var api = new Builder()
    .UseContainer()
    .WithName("api")
    .UseImage("myapi:latest")
    .UseNetwork(frontendNet)
    .UseIpV4("10.40.0.10")
    .ExposePort(8080)
    .Build()
    .Start();

// Connect to backend network as well
api.NetworkConnect(backendNet, "10.41.0.10");

// Database only on backend network
using var db = new Builder()
    .UseContainer()
    .WithName("db")
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    .UseNetwork(backendNet)
    .UseIpV4("10.41.0.20")
    .Build()
    .Start();

// API can reach both frontend and backend
// DB is only accessible from backend network
```

## Network Discovery

### List Networks

```csharp
var networks = await dockerHost.NetworksAsync();
foreach (var net in networks)
{
    Console.WriteLine($"Network: {net.Name} ({net.Driver})");
}
```

### Inspect Network

```csharp
using var network = new Builder()
    .UseNetwork("inspect-me")
    .UseSubnet("10.50.0.0/16")
    .Build();

var info = network.GetConfiguration();
Console.WriteLine($"Name: {info.Name}");
Console.WriteLine($"Driver: {info.Driver}");
Console.WriteLine($"Subnet: {info.IPAM?.Config?.FirstOrDefault()?.Subnet}");
```

## DNS and Aliases

### Container Aliases

```csharp
using var network = new Builder()
    .UseNetwork("aliased-net")
    .Build();

using var container = new Builder()
    .UseContainer()
    .WithName("myservice")
    .UseImage("nginx:alpine")
    .UseNetwork(network)
    .WithNetworkAlias("web", "frontend", "nginx")  // Multiple aliases
    .Build()
    .Start();

// Container reachable as: myservice, web, frontend, nginx
```

## Microservices Example

```csharp
// Create isolated network for microservices
using var network = new Builder()
    .UseNetwork("microservices")
    .UseSubnet("10.100.0.0/16")
    .Build();

// API Gateway
using var gateway = new Builder()
    .UseContainer()
    .WithName("gateway")
    .UseImage("kong:latest")
    .UseNetwork(network)
    .UseIpV4("10.100.0.10")
    .ExposePort(8000)
    .ExposePort(8443)
    .Build()
    .Start();

// User Service
using var userService = new Builder()
    .UseContainer()
    .WithName("user-service")
    .UseImage("user-service:latest")
    .UseNetwork(network)
    .UseIpV4("10.100.1.10")
    .Build()
    .Start();

// Order Service
using var orderService = new Builder()
    .UseContainer()
    .WithName("order-service")
    .UseImage("order-service:latest")
    .UseNetwork(network)
    .UseIpV4("10.100.2.10")
    .Build()
    .Start();

// Product Service
using var productService = new Builder()
    .UseContainer()
    .WithName("product-service")
    .UseImage("product-service:latest")
    .UseNetwork(network)
    .UseIpV4("10.100.3.10")
    .Build()
    .Start();

// Services communicate via DNS names or static IPs
// Gateway at 10.100.0.10 can route to all services
```

## Network Cleanup

### Auto-cleanup on Dispose

```csharp
using var network = new Builder()
    .UseNetwork("temp-network")
    .Build();

// Network removed when disposed
```

### Manual Removal

```csharp
var network = new Builder()
    .UseNetwork("manual-network")
    .Build();

// Use network...

// Remove manually
network.Remove();
```

## Testing with Isolated Networks

```csharp
public class NetworkIsolatedTest : IDisposable
{
    private readonly INetworkService _network;
    private readonly IContainerService _db;
    private readonly IContainerService _api;

    public NetworkIsolatedTest()
    {
        // Each test run gets isolated network
        var testId = Guid.NewGuid().ToString("N")[..8];

        _network = new Builder()
            .UseNetwork($"test-{testId}")
            .UseSubnet("10.200.0.0/24")
            .Build();

        _db = new Builder()
            .UseContainer()
            .WithName($"db-{testId}")
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .UseNetwork(_network)
            .UseIpV4("10.200.0.10")
            .WaitForPort("5432/tcp", 30000)
            .Build()
            .Start();

        _api = new Builder()
            .UseContainer()
            .WithName($"api-{testId}")
            .UseImage("myapi:test")
            .WithEnvironment("DB_HOST=10.200.0.10")
            .UseNetwork(_network)
            .ExposePort(8080)
            .WaitForPort("8080/tcp", 30000)
            .Build()
            .Start();
    }

    [Fact]
    public async Task Api_CanConnectToDatabase()
    {
        var endpoint = _api.ToHostExposedEndpoint("8080/tcp");
        var response = await $"http://localhost:{endpoint.Port}/health".WgetAsync();
        Assert.Contains("healthy", response);
    }

    public void Dispose()
    {
        _api?.Dispose();
        _db?.Dispose();
        _network?.Dispose();
    }
}
```

## Next Steps

- [Volumes](volumes.html) - Data persistence
- [Containers](containers.html) - Container management
- [Docker Compose](compose.html) - Multi-container orchestration
