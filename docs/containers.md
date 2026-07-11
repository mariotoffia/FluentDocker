---
layout: default
title: Containers
nav_order: 4
---

# Container Management

Complete guide to creating, configuring, and managing containers with FluentDocker v3.

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> it from source — see [Consume the preview](https://mariotoffia.github.io/FluentDocker/getting-started.html#consume-the-preview). The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## Step by Step

Basics: [Kernel Setup](#kernel-setup), [Container Lifecycle](#container-lifecycle), [Port Exposure](#port-exposure), [Environment Variables](#environment-variables); intermediate: [Wait Strategies](#wait-strategies), [Execute Commands](#execute-commands), [Container Logs](#container-logs), [Inspecting Container Info](#inspecting-container-info), [Cleanup and Dispose Behavior](#cleanup-and-dispose-behavior); advanced: [Resource Limits](#resource-limits), [Advanced Container Options](#advanced-container-options), [Container Existence Behavior](#container-existence-behavior), [File Operations](#file-operations).

## Kernel Setup

Before building any containers, create a kernel. Multiple kernels per application
are supported. The kernel manages driver instances and provides access to
container runtimes.

```csharp
using System;
using System.Linq;
using FluentDocker.Kernel;
using FluentDocker.Builders;
using FluentDocker.Services;            // ServiceRunningState
using FluentDocker.Services.Extensions; // ToHostExposedEndpointAsync

// Create kernel (multiple kernels per app are supported)
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

All subsequent examples assume this `kernel` variable is available.

## Container Lifecycle

### Create and Start

In v3, `BuildAsync()` both creates and starts containers automatically. The result is a
`BuildResults` object containing all built services.

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine"))
    .BuildAsync();

var container = results.Containers.First();
// Container is already running at this point
```

> A synchronous `.Build()` / `Dispose()` path exists for sync-only code, but it blocks on the async pipeline; prefer `BuildAsync()` with `await using`.

Dispose hooks run for the service lifecycle even when a container is kept or reused.
If build fails after create/start, FluentDocker captures a bounded log tail, runs
service removal hooks, removes anonymous volumes, and honors `KeepContainer()`.

### Stop and Start Cycle

Since containers auto-start during `Build()`, use the container service to stop and
restart as needed.

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine"))
    .BuildAsync();

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
await container.UnpauseAsync();  // resume a paused container (StartAsync does not)
```

## Port Exposure

### Explicit Port Mapping

```csharp
// Map host port 8080 to container port 80
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort(8080, 80))
    .BuildAsync();

// Access at http://localhost:8080
```

### Host-First Mapping with WithPort

`WithPort(hostPort, containerPort)` maps a host port to a container port, host-first — the
same order as `docker -p host:container` and `ExposePort`. The host port may carry an
interface (`"127.0.0.1:8080"`); the container port takes an optional protocol (`"80/tcp"`).

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithPort("8080", "80/tcp")               // host 8080 -> container 80
        .WithPort("127.0.0.1:9090", "9090/tcp"))  // bind loopback only
    .BuildAsync();
```

> **Breaking change (3.2.0-preview):** `WithPort` is now host-first. Stable 3.0/3.1 took
> `(containerPort, hostPort)`, so review every call after upgrading. `ExposePort` is unchanged.

### Random Port Assignment

```csharp
// Let Docker assign a random host port
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80"))
    .BuildAsync();

var container = results.Containers.First();

// Get the assigned port
var endpoint = await container.ToHostExposedEndpointAsync("80/tcp");
Console.WriteLine($"Port: {endpoint.Port}");
```

### Multiple Ports

Call `ExposePort` (or `WithPort`) once per mapping — `.ExposePort(8080, 80).ExposePort(8443, 443)`
— then resolve each with `ToHostExposedEndpointAsync("80/tcp")` after the container starts.

## Environment Variables

Each call to `WithEnvironment()` sets one variable. Two overloads are available:
`WithEnvironment("KEY=VALUE")` and `WithEnvironment("KEY", "VALUE")`.

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WithEnvironment("POSTGRES_USER", "myuser")
        .WithEnvironment("POSTGRES_DB", "mydb")
        .WithEnvironment("PGDATA=/var/lib/postgresql/data/pgdata"))
    .BuildAsync();
```

## Wait Strategies

Builder wait methods (`WaitForPort`, `WaitForProcess`, `WaitForLogMessage`,
`WaitForHealthy`, `WaitForHttp`, `Wait`) fail `BuildAsync()` with
`FluentDockerException`; when logs are available, the exception includes a
container log tail. Service extension waits (`container.WaitForPortAsync()` and siblings) return `false` on timeout and throw for cancellation or non-transient driver errors; an unexposed or mistyped port burns the full timeout and returns `false` (late port bindings are legal). They also fail fast: if the container reaches a terminal state (`exited`/`dead`) before becoming ready, the wait throws `FluentDockerException` carrying the exit code and a log tail instead of polling to timeout — except `WaitForLogMessageAsync`, which honors a message already in the logs (a short-lived container that logs the awaited line then exits still returns `true`).

### Wait for Port

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000))
    .BuildAsync();
```

### Wait for Process

`WaitForProcess` runs `pgrep -f` inside the container. Distroless and scratch
images usually do not ship `pgrep`; prefer a log, health, HTTP, or custom wait
for those images.

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WaitForProcess("postgres", 30000))
    .BuildAsync();
```

### Wait for Log Message

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WaitForLogMessage("database system is ready", 30000))
    .BuildAsync();
```

### Wait for Healthy

Waits for the container's Docker HEALTHCHECK to report healthy.

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .WaitForHealthy(60000))
    .BuildAsync();
```

### Wait for HTTP

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExposePort("8080")
        .WaitForHttp("8080/tcp", "/health", 30000))
    .BuildAsync();
```

Use `WaitForHttpUrl(...)` only when you already have a full URL and need advanced
HTTP options such as method, request body, or a custom continuation.

### Custom Wait Function

The `.Wait()` lambda receives the container service and an iteration counter. Return values:
- **Negative** (e.g. `-1`): success, stop waiting
- **Zero** (`0`): not ready, wait the configured poll interval (default 500 ms) before retrying
- **Positive** (e.g. `500`): not ready, wait that many milliseconds before retry

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExposePort("8080")
        .Wait((service, iteration) =>
        {
            return service.State == ServiceRunningState.Running && iteration > 2
                ? -1
                : 500;
        }))
    .BuildAsync();
```

## File Operations

> The copy-from and export lifecycle snippets below are verified by `ContainersDocSnippetsTests` integration tests, so their documented behavior can't drift from the implementation.

### Copy to Container on Start

Files are copied after the container starts (lifecycle hook).

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .CopyToOnStart("/local/nginx.conf", "/etc/nginx/nginx.conf")
        .CopyToOnStart("/local/html/", "/usr/share/nginx/html/"))
    .BuildAsync();
```

### Copy from Container on Dispose

Files are copied from the container before it is removed. A **directory** source (or a
destination ending in a separator) copies recursively into the destination directory; a
**single-file** source copies to the destination as a **file** when the destination is a
file path — it is not wrapped in a directory named after the target.

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .CopyFromOnDispose("/app/logs/", "/local/artifacts/logs/")   // directory → directory
        .CopyFromOnDispose("/app/report.xml", "/local/artifacts/report.xml")) // file → file
    .BuildAsync();

// Run tests...
// When disposed, the logs directory and the single report file are copied out.
```

### Export on Dispose

Export the entire container filesystem as a tar archive on dispose. By default the tar is
written to the exact path you supply (the parent directory is created if needed):

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExportOnDispose("/local/artifacts/container.tar"))
    .BuildAsync();
```

With a condition and `explode: true`, the archive is **extracted into** the supplied
directory path instead of being written as a single `.tar` file:

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExportOnDispose("/local/artifacts/", svc => svc.State == ServiceRunningState.Running, explode: true))
    .BuildAsync();
```

## Execute Commands

> The `ExecuteOnRunning` argv snippet below is verified by `ContainersDocSnippetsTests` integration tests, so its documented quoting behavior can't drift from the implementation.

### On Running (Lifecycle Hook)

Run a command once the container is **ready** — after all wait conditions pass, not merely
after start. The command runs **exactly once**, and a non-zero exit **propagates as an
exception**. Each string is a separate **argv token**: the shared command renderer quotes
any token containing spaces, so the final `"CREATE DATABASE mydb;"` stays a single
argument rather than being split on whitespace.

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000)
        .ExecuteOnRunning("psql", "-U", "postgres", "-c", "CREATE DATABASE mydb;"))
    .BuildAsync();
```

### On Disposing (Lifecycle Hook)

Run a command on the **Removing** lifecycle before FluentDocker stops the container, so
`docker exec` still has a running target. The same argv rules apply: `"echo 'shutting down' >> /app/log.txt"`
is one argv token passed to `sh -c`, not three separate arguments.

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExecuteOnDisposing("sh", "-c", "echo 'shutting down' >> /app/log.txt"))
    .BuildAsync();
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

## Names, Labels, and Configuration

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .WithName("my-app-container")
        .UseImage("nginx:alpine")
        .WithLabel("app", "myapp")
        .WithLabel("version", "1.0.0"))
    .BuildAsync();

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

To get the **host** port a container port is mapped to, use `ToHostExposedEndpointAsync`
(see [Port Exposure](#port-exposure)):

```csharp
var endpoint = await container.ToHostExposedEndpointAsync("80/tcp"); // e.g. 127.0.0.1:49162
Console.WriteLine($"Reachable at {endpoint.Address}:{endpoint.Port}");
```

## Resource Limits

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .WithMemoryLimit(512 * 1024 * 1024)  // 512MB
        .WithCpuShares(1024))                 // CPU shares
    .BuildAsync();
```

## Advanced Container Options

The following methods configure additional container properties inside the
`UseContainer(c => ...)` lambda:

```csharp
await using var results = await new Builder()
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
        .WithEntrypoint("/bin/myapp", "--serve")  // Override image ENTRYPOINT
        .WithRestartPolicy("unless-stopped")      // no | always | on-failure:5 | unless-stopped
        .WithPlatform("linux/arm64")              // Pull/run a specific arch (multi-arch images)
        .WithDevice("/dev/fuse")                  // Map a host device into the container
        .WithShmSize(256 * 1024 * 1024)           // /dev/shm size in bytes
        .WithLink("db", "database")               // Legacy container link (prefer networks)
        .WithTty()                                // Allocate a pseudo-TTY (docker run -t)
        .WithInteractive()                        // Keep STDIN open (docker run -i)
        .WithHealthCheck("curl -f http://localhost/health || exit 1", "10s", "2s", retries: 3)
        .WithNetwork("my-network")            // Attach to named network
        .WithNetworkAlias("my-network", "app") // DNS alias on network
        .WithIPv4("10.18.0.22"))               // Static IP (requires custom subnet)
    .BuildAsync();
```

> `WithHealthCheck` runs the command through `CMD-SHELL`, so the image must contain `/bin/sh`. Distroless and `scratch` images have no shell — the check never reports healthy. Use an image with a shell, or drop the health check and wait on a port or log line instead.

> `WithInteractive()` with `WithTty()` keeps a short-lived base image alive so you can `ExecuteAsync` into it.
> `WithLinks("db", "cache")` links several containers; `WithWaitPollInterval(250)` sets the wait poll gap (default 500 ms).

## Container Existence Behavior

When a container with the same name already exists, control what happens:

`ReuseIfExists()` matches names case-sensitively and reuses the container as-is
(config differences ignored). A running match still runs wait conditions; a stopped match runs the full start sequence.

```csharp
// Reuse the existing container if one matches by name
.ReuseIfExists()

// Destroy the existing container and create a new one
.DestroyIfExists(force: true, removeVolumes: true)

// Always pull the latest image before creating
.ForcePullImage()
```

## Cleanup and Dispose Behavior

By default, containers are stopped and removed when `BuildResults` is disposed — as
**best-effort, time-bounded** cleanup (each service gets a 60 s budget; a wedged daemon
leaves the resource retained for retry rather than hanging). See
[Disposal is time-bounded → Disposing `BuildResults`](service-lifecycle.md#disposing-buildresults)
for the per-service budget, the retry/second-call contract, sync-vs-async differences, and
`BuildAsync(cleanupTimeout)`.

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

`WithVolume(source, target)` takes either a host path (bind mount) or a named volume as the
source — `.WithVolume("/local/html", "/usr/share/nginx/html")` for a bind mount, or
`.WithVolume("pgdata", "/var/lib/postgresql/data")` for a named volume. See
[Volumes](volumes.md) for the full guide.

## Multiple Containers

Build multiple containers in a single builder call.

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    // Create the network first so both containers can attach to it
    .UseNetwork(n => n
        .WithName("my-network")
        .RemoveOnDispose())
    .UseContainer(c => c
        .WithName("db")
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WithNetwork("my-network")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000))
    .UseContainer(c => c
        .WithName("app")
        .UseImage("myapp:latest")
        .WithEnvironment("DATABASE_HOST", "db")
        .WithNetwork("my-network")
        .ExposePort(8080, 80))
    .BuildAsync();

var db = results.GetContainer("db");
var app = results.GetContainer("app");
```

## Next Steps

- [Networking](networking.md) - Custom networks and static IPs
- [Volumes](volumes.md) - Data persistence
- [Docker Compose](compose.md) - Multi-container orchestration
