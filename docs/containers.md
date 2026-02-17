---
layout: default
title: Containers
nav_order: 4
---

# Container Management

Complete guide to creating, configuring, and managing containers with FluentDocker v3.

## Step by Step

- Basics: [Kernel Setup](#kernel-setup), [Container Lifecycle](#container-lifecycle), [Port Exposure](#port-exposure), [Environment Variables](#environment-variables)
- Intermediate: [Wait Strategies](#wait-strategies), [Execute Commands](#execute-commands), [Container Logs](#container-logs), [Cleanup and Dispose Behavior](#cleanup-and-dispose-behavior)
- Advanced: [Resource Limits](#resource-limits), [Advanced Container Options](#advanced-container-options), [Container Existence Behavior](#container-existence-behavior), [File Operations](#file-operations)

## Kernel Setup

Before building any containers, create a kernel. Multiple kernels per application
are supported. The kernel manages driver instances and provides access to
container runtimes.

```csharp
using FluentDocker.Kernel;
using FluentDocker.Builders;

// Create kernel (multiple kernels per app are supported)
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();
```

All subsequent examples assume this `kernel` variable is available.

## Container Lifecycle

### Create and Start

In v3, `Build()` both creates and starts containers automatically. The result is a
`BuildResults` object containing all built services.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine"))
    .Build();

var container = results.Containers.First();
// Container is already running at this point
```

### Stop and Start Cycle

Since containers auto-start during `Build()`, use the container service to stop and
restart as needed.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine"))
    .Build();

var container = results.Containers.First();

// Stop the running container
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
container.Pause();
container.Start();  // Start() also resumes from Pause
```

## Port Exposure

### Explicit Port Mapping

```csharp
// Map host port 8080 to container port 80
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort(8080, 80))
    .Build();

// Access at http://localhost:8080
```

### Random Port Assignment

```csharp
// Let Docker assign a random host port
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80"))
    .Build();

var container = results.Containers.First();

// Get the assigned port
var endpoint = container.ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"Port: {endpoint.Port}");
```

### Multiple Ports

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExposePort(8080, 80)
        .ExposePort(8443, 443)
        .ExposePort(9090, 9090))
    .Build();

var container = results.Containers.First();
var httpEndpoint = container.ToHostExposedEndpoint("80/tcp");
var httpsEndpoint = container.ToHostExposedEndpoint("443/tcp");
var metricsEndpoint = container.ToHostExposedEndpoint("9090/tcp");
```

## Environment Variables

Each call to `WithEnvironment()` sets one variable. Two overloads are available:
`WithEnvironment("KEY=VALUE")` and `WithEnvironment("KEY", "VALUE")`.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WithEnvironment("POSTGRES_USER", "myuser")
        .WithEnvironment("POSTGRES_DB", "mydb")
        .WithEnvironment("PGDATA=/var/lib/postgresql/data/pgdata"))
    .Build();
```

## Wait Strategies

### Wait for Port

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000))
    .Build();
```

### Wait for Process

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WaitForProcess("postgres", 30000))
    .Build();
```

### Wait for Log Message

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WaitForLogMessage("database system is ready", 30000))
    .Build();
```

### Wait for Healthy

Waits for the container's Docker HEALTHCHECK to report healthy.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .WaitForHealthy(60000))
    .Build();
```

### Wait for HTTP

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExposePort("8080")
        .WaitForHttp("8080/tcp", "/health", 30000))
    .Build();
```

### Custom Wait Function

The `.Wait()` lambda receives the container service and an iteration counter. Return values:
- **Negative** (e.g. `-1`): success, stop waiting
- **Zero** (`0`): not ready, retry immediately
- **Positive** (e.g. `500`): not ready, wait that many milliseconds before retry

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExposePort("8080")
        .Wait((service, iteration) =>
        {
            try
            {
                var ep = service.ToHostExposedEndpoint("8080/tcp");
                var response = $"http://localhost:{ep.Port}/health".Wget();
                return response.Contains("ok") ? -1 : 500;
            }
            catch
            {
                return 500;
            }
        }))
    .Build();
```

## File Operations

### Copy to Container on Start

Files are copied after the container starts (lifecycle hook).

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .CopyToOnStart("/local/nginx.conf", "/etc/nginx/nginx.conf")
        .CopyToOnStart("/local/html/", "/usr/share/nginx/html/"))
    .Build();
```

### Copy from Container on Dispose

Files are copied from the container before it is removed.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .CopyFromOnDispose("/app/logs/", "/local/artifacts/logs/")
        .CopyFromOnDispose("/app/coverage/", "/local/artifacts/coverage/"))
    .Build();

// Run tests...
// When disposed, logs and coverage are copied out
```

### Export on Dispose

Export the entire container filesystem as a tar archive on dispose.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExportOnDispose("/local/artifacts/container.tar"))
    .Build();
```

With a condition and explode (extract tar contents):

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExportOnDispose("/local/artifacts/", svc => svc.State == ServiceRunningState.Running, explode: true))
    .Build();
```

## Execute Commands

### On Running (Lifecycle Hook)

Execute a command automatically after the container starts.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WaitForPort("5432/tcp", 30000)
        .ExecuteOnRunning("psql", "-U", "postgres", "-c", "CREATE DATABASE mydb;"))
    .Build();
```

### On Disposing (Lifecycle Hook)

Execute a command before the container is removed.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExecuteOnDisposing("sh", "-c", "echo 'shutting down' >> /app/log.txt"))
    .Build();
```

### Ad-hoc Commands on a Running Container

```csharp
var container = results.Containers.First();

var result = await container.ExecAsync("echo", "Hello World");
Console.WriteLine(result);  // "Hello World"

// Shell commands
var output = await container.ExecAsync("sh", "-c", "ls -la /app && cat /app/config.json");

// Redis example
await container.ExecAsync("redis-cli", "SET", "mykey", "myvalue");
var value = await container.ExecAsync("redis-cli", "GET", "mykey");
```

## Names, Labels, and Configuration

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("my-app-container")
        .UseImage("nginx:alpine")
        .WithLabel("app", "myapp")
        .WithLabel("version", "1.0.0"))
    .Build();

var container = results.Containers.First();
var config = container.GetConfiguration(fresh: true);
Console.WriteLine($"ID: {config.Id}, Name: {config.Name}, State: {config.State.Status}");
```

## Resource Limits

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .WithMemoryLimit(512 * 1024 * 1024)  // 512MB
        .WithCpuShares(1024))                 // CPU shares
    .Build();
```

## Advanced Container Options

The following methods configure additional container properties inside the
`UseContainer(c => ...)` lambda:

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .WithPrivileged()                     // Full host access (use with caution)
        .WithWorkingDirectory("/app")         // Default working directory
        .WithCommand("sh", "-c", "sleep 3600") // Override CMD
        .WithHostname("app-host")
        .WithUser("appuser")
        .WithNetwork("my-network")            // Attach to named network
        .WithNetworkAlias("my-network", "app") // DNS alias on network
        .UseIpV4("10.18.0.22"))               // Static IP (requires custom subnet)
    .Build();
```

## Container Existence Behavior

When a container with the same name already exists, control what happens:

```csharp
// Reuse the existing container if one matches by name
.ReuseIfExists()

// Destroy the existing container and create a new one
.DestroyIfExists(force: true, removeVolumes: true)

// Always pull the latest image before creating
.ForcePullImage()
```

## Cleanup and Dispose Behavior

By default, containers are stopped and removed when `BuildResults` is disposed.
Use these methods inside the `UseContainer(c => ...)` lambda to customize:

```csharp
.KeepContainer()            // Don't remove container on dispose (for debugging)
.KeepRunning()              // Don't stop container on dispose
.WithAutoRemove()           // Docker-level auto-remove on stop
.DeleteVolumeOnDispose()    // Remove anonymous volumes on dispose
.DeleteNamedVolumeOnDispose() // Remove named volumes on dispose
```

## Container Logs

```csharp
var container = results.Containers.First();

var logs = await container.GetLogsAsync();
foreach (var line in logs)
{
    Console.WriteLine(line);
}
```

## Volumes (Bind Mounts and Named Volumes)

```csharp
// Bind mount
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithVolume("/local/html", "/usr/share/nginx/html"))
    .Build();

// Named volume
using var results2 = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WithVolume("pgdata", "/var/lib/postgresql/data"))
    .Build();
```

## Multiple Containers

Build multiple containers in a single builder call.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("db")
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WaitForPort("5432/tcp", 30000))
    .UseContainer(c => c
        .WithName("app")
        .UseImage("myapp:latest")
        .WithEnvironment("DATABASE_HOST", "db")
        .WithNetwork("my-network")
        .ExposePort(8080, 80))
    .Build();

var db = results.GetContainer("db");
var app = results.GetContainer("app");
```

## Next Steps

- [Networking](networking.html) - Custom networks and static IPs
- [Volumes](volumes.html) - Data persistence
- [Docker Compose](compose.html) - Multi-container orchestration
