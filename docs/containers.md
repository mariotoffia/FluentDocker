---
layout: default
title: Containers
nav_order: 4
---

# Container Management

Complete guide to creating, configuring, and managing containers with FluentDocker v3.

## Step by Step

- Basics: [Kernel Setup](#kernel-setup), [Container Lifecycle](#container-lifecycle), [Port Exposure](#port-exposure), [Environment Variables](#environment-variables)
- Intermediate: [Wait Strategies](#wait-strategies), [Execute Commands](#execute-commands), [Container Logs](#container-logs), [Inspecting Container Info](#inspecting-container-info), [Cleanup and Dispose Behavior](#cleanup-and-dispose-behavior)
- Advanced: [Resource Limits](#resource-limits), [Advanced Container Options](#advanced-container-options), [Container Existence Behavior](#container-existence-behavior), [File Operations](#file-operations)

## Kernel Setup

Before building any containers, create a kernel. Multiple kernels per application
are supported. The kernel manages driver instances and provides access to
container runtimes.

```csharp
using System;
using System.Linq;
using FluentDocker.Kernel;
using FluentDocker.Builders;
using FluentDocker.Services.Extensions; // ToHostExposedEndpoint

// Create kernel (multiple kernels per app are supported)
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

`ReuseIfExists()` matches names case-sensitively. If the existing container is already
running, waits are skipped and requested config differences are ignored.

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

Dispose hooks run for the service lifecycle even when a container is kept or reused.
If build fails after create/start, FluentDocker captures a bounded log tail, runs
service removal hooks, removes anonymous volumes, and honors `KeepContainer()`.

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
await container.StopAsync();

// Start again
await container.StartAsync();
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
await container.PauseAsync();
await container.StartAsync();  // StartAsync() also resumes from Pause
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

Port APIs use their documented order: `ExposePort(hostPort, containerPort)` but
`WithPort(containerPort, hostPort)`.

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
                using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var response = FluentDocker.Common.SharedHttpClient.Instance.GetStringAsync(
                    $"http://localhost:{ep.Port}/health", requestCts.Token)
                    .GetAwaiter().GetResult();
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

> The copy-from and export lifecycle snippets below are verified by the
> `ContainersDocSnippetsTests` integration tests, so their documented behavior cannot
> silently drift from the implementation.

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

Files are copied from the container before it is removed. A **directory** source (or a
destination ending in a separator) copies recursively into the destination directory; a
**single-file** source copies to the destination as a **file** when the destination is a
file path — it is not wrapped in a directory named after the target.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .CopyFromOnDispose("/app/logs/", "/local/artifacts/logs/")   // directory → directory
        .CopyFromOnDispose("/app/report.xml", "/local/artifacts/report.xml")) // file → file
    .Build();

// Run tests...
// When disposed, the logs directory and the single report file are copied out.
```

### Export on Dispose

Export the entire container filesystem as a tar archive on dispose. By default the tar is
written to the exact path you supply (the parent directory is created if needed):

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExportOnDispose("/local/artifacts/container.tar"))
    .Build();
```

With a condition and `explode: true`, the archive is **extracted into** the supplied
directory path instead of being written as a single `.tar` file:

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExportOnDispose("/local/artifacts/", svc => svc.State == ServiceRunningState.Running, explode: true))
    .Build();
```

## Execute Commands

> The `ExecuteOnRunning` argv snippet below is verified by the
> `ContainersDocSnippetsTests` integration tests, so its documented quoting behavior
> cannot silently drift from the implementation.

### On Running (Lifecycle Hook)

Run a command once the container is **ready** — after all wait conditions pass, not merely
after start. The command runs **exactly once**, and a non-zero exit **propagates as an
exception**. Each string is a separate **argv token**: the shared command renderer quotes
any token containing spaces, so the final `"CREATE DATABASE mydb;"` stays a single
argument rather than being split on whitespace.

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

Run a command on the **Removing** lifecycle before FluentDocker stops the container, so
`docker exec` still has a running target. The same argv rules apply: `"echo 'shutting down' >> /app/log.txt"`
is one argv token passed to `sh -c`, not three separate arguments.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExecuteOnDisposing("sh", "-c", "echo 'shutting down' >> /app/log.txt"))
    .Build();
```

### Ad-hoc Commands on a Running Container

`ExecuteAsync(string)` shell-parses the command (honoring quotes) and returns the
container's **stdout** as a string.

```csharp
var container = results.Containers.First();

var result = await container.ExecuteAsync("echo Hello World");
Console.WriteLine(result);  // stdout: "Hello World"

// Quotes are honored, so a whole shell program can be passed to sh -c:
var listing = await container.ExecuteAsync("sh -c 'ls -la /app && cat /app/config.json'");

// Redis example
await container.ExecuteAsync("redis-cli SET mykey myvalue");
var value = await container.ExecuteAsync("redis-cli GET mykey");
```

The `ExecuteOnRunning` / `ExecuteOnDisposing` hooks above take a **`params string[]`**
argv instead: each element is one argument and is never re-split, which is why
`"CREATE DATABASE mydb;"` stays a single token.

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

### Inspecting Container Info

`GetConfiguration(fresh: true)` (or the async `GetConfigurationAsync()`) returns the full
inspected `Container`, so you can read the creation time, the resolved image config,
environment, exposed ports, and labels without shelling out to `docker inspect`:

```csharp
var config = container.GetConfiguration(fresh: true);

Console.WriteLine($"Created:    {config.Created:O}");
Console.WriteLine($"Image:      {config.Config.Image}");
Console.WriteLine($"WorkingDir: {config.Config.WorkingDir}");

foreach (var env in config.Config.Env ?? Array.Empty<string>())
    Console.WriteLine($"Env:     {env}");

foreach (var port in config.Config.ExposedPorts?.Keys ?? Enumerable.Empty<string>())
    Console.WriteLine($"Exposed: {port}");

foreach (var (key, value) in config.Config.Labels ?? new Dictionary<string, string>())
    Console.WriteLine($"Label:   {key}={value}");
```

To get the **host** port a container port is mapped to, use `ToHostExposedEndpoint`
(see [Port Exposure](#port-exposure)):

```csharp
var endpoint = container.ToHostExposedEndpoint("80/tcp"); // e.g. 127.0.0.1:49162
Console.WriteLine($"Reachable at {endpoint.Address}:{endpoint.Port}");
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
        .WithDns("1.1.1.1")
        .WithStopSignal("SIGTERM")
        .WithHealthCheck("curl -f http://localhost/health || exit 1", "10s", "2s", retries: 3)
        .WithNetwork("my-network")            // Attach to named network
        .WithNetworkAlias("my-network", "app") // DNS alias on network
        .WithIPv4("10.18.0.22"))               // Static IP (requires custom subnet)
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
.KeepRunning()              // Don't stop or delete container on dispose
.WithAutoRemove()           // Docker-level auto-remove on stop
.DeleteVolumeOnDispose()    // Remove anonymous volumes on dispose
.DeleteNamedVolumeOnDispose() // Remove named volumes on dispose
```

## Container Logs

```csharp
var container = results.Containers.First();

var logs = await container.GetLogsAsync();
foreach (var line in logs.Split('\n'))
{
    Console.WriteLine(line);
}
```

`GetLogsAsync(follow: true)` is rejected on buffered drivers; use streaming APIs for follow mode.

Buffered Docker CLI logs are diagnostic-safe: huge output returns a bounded tail instead of
throwing, prefixed by `[FluentDocker: output truncated, showing last N chars]`. Foreground `RunAsync` and
`ExecAsync` use the same marker for large stdout/stderr tails; other buffered Docker CLI calls
still fail fast at their memory cap. If you need every byte, use `tail`, redirect in the
container, or stream logs (`StreamLogsAsync` tags stderr lines as `[stderr] ...`).

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

- [Networking](networking.md) - Custom networks and static IPs
- [Volumes](volumes.md) - Data persistence
- [Docker Compose](compose.md) - Multi-container orchestration
