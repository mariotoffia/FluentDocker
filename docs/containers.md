---
layout: default
title: Containers
nav_order: 3
---

# Container Management

Complete guide to creating, configuring, and managing containers with FluentDocker.

## Container Lifecycle

### Create and Start

```csharp
using FluentDocker.Builders;

// Create and start immediately
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .Build()
    .Start();
```

### Create Without Starting

```csharp
// Create container (does not start)
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .Build();

// Start later
container.Start();

// Stop
container.Stop();

// Start again
container.Start();
```

### Container States

```csharp
var state = container.State;
// ServiceRunningState.Running, Stopped, Paused, etc.

if (container.State == ServiceRunningState.Running)
{
    Console.WriteLine("Container is running");
}
```

### Pause and Resume

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .Build()
    .Start();

// Pause container processes
container.Pause();

// Resume
container.Resume();
```

## Port Exposure

### Explicit Port Mapping

```csharp
// Map host port 8080 to container port 80
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .ExposePort(8080, 80)
    .Build()
    .Start();

// Access at http://localhost:8080
```

### Random Port Assignment

```csharp
// Let Docker assign a random host port
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .ExposePort(80)  // Random host port -> container 80
    .Build()
    .Start();

// Get the assigned port
var endpoint = container.ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"Port: {endpoint.Port}");
// Access at http://localhost:{endpoint.Port}
```

### Multiple Ports

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .ExposePort(8080, 80)   // HTTP
    .ExposePort(8443, 443)  // HTTPS
    .ExposePort(9090, 9090) // Metrics
    .Build()
    .Start();

var httpEndpoint = container.ToHostExposedEndpoint("80/tcp");
var httpsEndpoint = container.ToHostExposedEndpoint("443/tcp");
var metricsEndpoint = container.ToHostExposedEndpoint("9090/tcp");
```

### UDP Ports

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("coredns/coredns")
    .ExposePort(53, 53, "udp")  // DNS over UDP
    .ExposePort(53, 53, "tcp")  // DNS over TCP
    .Build()
    .Start();
```

## Environment Variables

### Single Variable

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    .Build()
    .Start();
```

### Multiple Variables

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")
    .WithEnvironment(
        "POSTGRES_PASSWORD=secret",
        "POSTGRES_USER=myuser",
        "POSTGRES_DB=mydb"
    )
    .Build()
    .Start();
```

### From Dictionary

```csharp
var env = new Dictionary<string, string>
{
    ["POSTGRES_PASSWORD"] = "secret",
    ["POSTGRES_USER"] = "myuser",
    ["POSTGRES_DB"] = "mydb",
    ["PGDATA"] = "/var/lib/postgresql/data/pgdata"
};

using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")
    .WithEnvironment(env)
    .Build()
    .Start();
```

## Wait Strategies

### Wait for Port

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    .ExposePort(5432)
    .WaitForPort("5432/tcp", 30000)  // Wait 30 seconds
    .Build()
    .Start();
```

### Wait for Process

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    .WaitForProcess("postgres", 30000)  // Wait for postgres process
    .Build()
    .Start();
```

### Wait for Log Message

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    .WaitForMessageInLog("database system is ready", 30000)
    .Build()
    .Start();
```

### Custom Wait Function

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .ExposePort(8080)
    .Wait((service, count) =>
    {
        // Custom health check logic
        try
        {
            var response = $"http://localhost:{service.ToHostExposedEndpoint("8080/tcp").Port}/health".Wget();
            return response.Contains("ok") ? 0 : 500;  // 0 = ready
        }
        catch
        {
            return 500;  // Not ready
        }
    }, 30000)
    .Build()
    .Start();
```

## File Operations

### Copy to Container (Before Start)

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .CopyOnStart("/local/nginx.conf", "/etc/nginx/nginx.conf")
    .CopyOnStart("/local/html/", "/usr/share/nginx/html/")
    .Build()
    .Start();
```

### Copy to Container (After Start)

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .Build()
    .Start();

// Copy single file
await container.CopyToAsync("/local/index.html", "/usr/share/nginx/html/index.html");

// Copy directory (recursive)
await container.CopyToAsync("/local/html/", "/usr/share/nginx/html/");
```

### Copy from Container

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .Build()
    .Start();

// Copy logs after tests
await container.CopyFromAsync("/app/logs/", "/local/test-logs/");
```

### Copy on Dispose

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .CopyOnDispose("/app/logs/", "/local/artifacts/logs/")
    .CopyOnDispose("/app/coverage/", "/local/artifacts/coverage/")
    .Build()
    .Start();

// Run tests...

// When disposed, logs and coverage are copied out
```

## Execute Commands

### Simple Command

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("alpine:latest")
    .Build()
    .Start();

var result = await container.ExecAsync("echo", "Hello World");
Console.WriteLine(result);  // "Hello World"
```

### Shell Commands

```csharp
var result = await container.ExecAsync("sh", "-c", "ls -la /app && cat /app/config.json");
```

### Database Commands

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    .WaitForPort("5432/tcp", 30000)
    .Build()
    .Start();

// Run SQL
var result = await container.ExecAsync(
    "psql", "-U", "postgres", "-c", "SELECT version();"
);
```

### Interactive Commands

```csharp
// Redis example
using var container = new Builder()
    .UseContainer()
    .UseImage("redis:alpine")
    .WaitForPort("6379/tcp", 30000)
    .Build()
    .Start();

await container.ExecAsync("redis-cli", "SET", "mykey", "myvalue");
var value = await container.ExecAsync("redis-cli", "GET", "mykey");
Console.WriteLine(value);  // "myvalue"
```

## Container Stats

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .Build()
    .Start();

// Get resource usage stats
var stats = await container.GetStatsAsync();

Console.WriteLine($"CPU: {stats.CpuPercent:F2}%");
Console.WriteLine($"Memory: {stats.MemoryUsage:N0} / {stats.MemoryLimit:N0} bytes");
Console.WriteLine($"Memory %: {stats.MemoryPercent:F2}%");
Console.WriteLine($"Network RX: {stats.NetworkRxBytes:N0} bytes");
Console.WriteLine($"Network TX: {stats.NetworkTxBytes:N0} bytes");
Console.WriteLine($"Block Read: {stats.BlockReadBytes:N0} bytes");
Console.WriteLine($"Block Write: {stats.BlockWriteBytes:N0} bytes");
```

## Container Configuration

### Get Configuration

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .Build()
    .Start();

// Get full configuration
var config = container.GetConfiguration(refresh: true);

Console.WriteLine($"ID: {config.Id}");
Console.WriteLine($"Name: {config.Name}");
Console.WriteLine($"Image: {config.Image}");
Console.WriteLine($"Created: {config.Created}");
Console.WriteLine($"State: {config.State.Status}");
```

### Labels

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .WithLabel("app", "myapp")
    .WithLabel("version", "1.0.0")
    .WithLabel("environment", "test")
    .Build()
    .Start();

// Labels are useful for filtering and identification
```

### Resource Limits

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .UseMemory(512 * 1024 * 1024)  // 512MB memory limit
    .UseCpu(1.5)                   // 1.5 CPU cores
    .Build()
    .Start();
```

### Ulimits

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .UseUlimit(Ulimit.NoFile, 65535, 65535)  // Max open files
    .Build()
    .Start();
```

## Health Checks

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .UseHealthCheck(
        cmd: "curl -f http://localhost/health || exit 1",
        interval: TimeSpan.FromSeconds(30),
        timeout: TimeSpan.FromSeconds(10),
        retries: 3,
        startPeriod: TimeSpan.FromSeconds(5)
    )
    .Build()
    .Start();

// Check health status
var config = container.GetConfiguration(refresh: true);
Console.WriteLine($"Health: {config.State.Health?.Status}");
```

## Container Logs

### Get All Logs

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .Build()
    .Start();

var logs = container.Logs();
foreach (var line in logs)
{
    Console.WriteLine(line);
}
```

### Stream Logs

```csharp
// Tail logs in real-time
using var logStream = container.Logs(follow: true, tail: 100);
await foreach (var line in logStream)
{
    Console.WriteLine(line);
}
```

## Naming Containers

```csharp
// Named container
using var container = new Builder()
    .UseContainer()
    .WithName("my-app-container")
    .UseImage("myapp:latest")
    .Build()
    .Start();

// Container can be referenced by name in networks
```

## Privileged Mode

```csharp
// Use with caution - grants full host access
using var container = new Builder()
    .UseContainer()
    .UseImage("docker:dind")
    .IsPrivileged()
    .Build()
    .Start();
```

## Working Directory

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("node:18-alpine")
    .UseWorkDir("/app")
    .Build()
    .Start();

// Commands execute in /app by default
```

## Entry Point Override

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("alpine:latest")
    .UseEntrypoint("sh", "-c")
    .UseCommand("echo 'Custom entrypoint' && sleep 3600")
    .Build()
    .Start();
```

## Cleanup

### Remove on Stop

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .RemoveOnDispose()  // Auto-remove when disposed
    .Build()
    .Start();

// Container is automatically removed when disposed
```

### Keep Container

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .KeepContainer()  // Don't remove on dispose
    .Build()
    .Start();

// Container remains after disposal (for debugging)
```

## Next Steps

- [Networking](networking.html) - Custom networks and static IPs
- [Volumes](volumes.html) - Data persistence
- [Docker Compose](compose.html) - Multi-container orchestration
