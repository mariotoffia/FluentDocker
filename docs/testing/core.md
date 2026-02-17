---
layout: default
title: Testing Core
parent: Testing
nav_order: 1
---

# FluentDocker Testing Core

The testing core lives inside the main `FluentDocker` assembly under the namespace
`FluentDocker.Testing.Core`. No separate NuGet package is needed.

## Step by Step

- Basics: [Core Types](#core-types), [Wait Conditions (Builder)](#wait-conditions-builder)
- Intermediate: [Lifecycle Hooks](#lifecycle-hooks), [Diagnostics](#diagnostics), [Usage Example](#usage-example)
- Advanced: [ResourceLifecycle (Advanced)](#resourcelifecycle-advanced)

## Core Types

### `ITestResource`

```csharp
public interface ITestResource : IAsyncDisposable
{
    bool IsInitialized { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
```

All test resources implement this interface. Initialization creates, starts, and waits for
readiness. Disposal stops and removes the resource.

### Resource Types

| Resource | Purpose |
|---|---|
| `ContainerResource` | Single container with wait conditions |
| `ComposeResource` | Docker Compose project |
| `TopologyResource` | Multi-container topology with networks/volumes |
| `SwarmStackResource` | Docker Swarm stack via `docker stack deploy` |
| `PodmanKubernetesResource` | Podman `kube play` / `kube down` |

### `DockerResourceOptions`

Shared configuration for all resources:

```csharp
var options = new DockerResourceOptions
{
    Driver = DriverSelection.DockerCli(),       // or Default, DockerApi, PodmanCli
    ForceRemoveOnDispose = true,                // force-remove on cleanup failure
    InitializationTimeout = TimeSpan.FromMinutes(2),
    CaptureLogsOnFailure = true,                // collect logs for diagnostics
    MaxDiagnosticLogLines = 200                 // truncate logs beyond this
};
```

### `DriverSelection`

Controls which driver a resource uses:

```csharp
DriverSelection.Default          // kernel's default driver
DriverSelection.DockerCli()      // Docker CLI driver
DriverSelection.DockerApi()      // Docker REST API driver
DriverSelection.PodmanCli()      // Podman CLI driver
DriverSelection.Specific("id")   // any registered driver by ID
```

### ExpectedType Validation

When using `DriverSelection.DockerCli()`, `DockerApi()`, or `PodmanCli()`, the
`ExpectedType` property is set automatically. During initialization, the resource
validates that the resolved driver pack's type matches:

```csharp
// This will throw if "my-driver" is actually a PodmanCli driver
var options = new DockerResourceOptions
{
    Driver = DriverSelection.DockerCli("my-driver")
};
```

The validation runs before preflight, so mismatches fail fast with a clear error.

### MaxDiagnosticLogLines

When `CaptureLogsOnFailure` is true and initialization fails, diagnostic logs are
automatically truncated to `MaxDiagnosticLogLines` (default: 200). This prevents
excessive memory usage from very large log outputs. The truncated output includes
a count of omitted lines.

## Wait Conditions (Builder)

The container builder provides built-in wait conditions that block until the
container is ready. These execute after the container starts during
`ProvisionAsync`:

### Wait for a Port

```csharp
builder.UseImage("redis:alpine")
       .WaitForPort("6379/tcp", timeoutMs: 30_000);
```

### Wait for a Health Check

Polls `docker inspect` until the container's health status is `healthy`.
Requires a `HEALTHCHECK` instruction in the image:

```csharp
builder.UseImage("postgres:16")
       .WithEnvironment("POSTGRES_PASSWORD=test")
       .WaitForHealthy(timeoutMs: 60_000);
```

### Wait for a Log Message

Watches container logs for a specific string:

```csharp
builder.UseImage("postgres:16")
       .WithEnvironment("POSTGRES_PASSWORD=test")
       .WaitForLogMessage("ready to accept connections", timeoutMs: 60_000);
```

### Wait for an HTTP Endpoint

Makes HTTP requests until a successful response:

```csharp
builder.UseImage("my-api:latest")
       .ExposePort("8080")
       .WaitForHttp("8080/tcp", path: "/health", timeoutMs: 30_000);
```

Advanced HTTP wait with custom method and response handling:

```csharp
builder.WaitForHttp(
    url: "http://localhost:8080/ready",
    timeoutMs: 30_000,
    method: HttpMethod.Post,
    contentType: "application/json",
    body: "{\"check\":\"deep\"}",
    continuation: (response, attempt) =>
        response.Code == System.Net.HttpStatusCode.OK ? -1 : 1000);
```

The `continuation` function returns `-1` for success, or the delay in ms
before the next attempt.

### Wait for a Process

Checks that a specific process is running inside the container:

```csharp
builder.UseImage("nginx:alpine")
       .WaitForProcess("nginx", timeoutMs: 30_000);
```

### Custom Lambda Wait

For arbitrary conditions:

```csharp
builder.Wait((container, attempt) =>
{
    // Return -1 to signal success
    // Return 0 to continue immediately
    // Return N > 0 to wait N ms before next poll
    if (attempt > 30) return -1; // give up after 30 attempts
    return 1000; // poll every second
});
```

## Lifecycle Hooks

All resources support four lifecycle hooks. Hooks are chainable and receive
the resource instance:

```csharp
resource
    .OnBeforeInitialize(async r => { /* before preflight + provisioning */ })
    .OnAfterReady(async r =>        { /* resource is up — verify readiness */ })
    .OnBeforeDispose(async r =>     { /* before teardown — flush data, etc */ })
    .OnAfterDispose(async r =>      { /* after cleanup — log final state */ });
```

### Hook Execution Behavior

| Phase | On throw | Effect |
|---|---|---|
| `OnBeforeInitialize` | Aborts init | Diagnostics captured, exception propagates |
| `OnAfterReady` | Aborts init | `IsInitialized` stays false, diagnostics captured |
| `OnBeforeDispose` | Suppressed | Cleanup proceeds regardless |
| `OnAfterDispose` | Suppressed | Final state is already cleaned up |

### Using OnAfterReady for Custom Wait Strategies

When the built-in wait conditions aren't sufficient (e.g., verifying actual
database connectivity, checking a custom protocol), use `OnAfterReady` to
add a post-start readiness check.

**Database connectivity check:**

```csharp
var resource = new ContainerResource(kernel,
    b => b.UseImage("postgres:16")
          .WithEnvironment("POSTGRES_PASSWORD=test")
          .ExposePort("5432"));

resource.OnAfterReady(async _ =>
{
    var endpoint = resource.Container.ToHostExposedEndpoint("5432/tcp");
    var connStr = $"Host=localhost;Port={endpoint.Port};" +
                  "Username=postgres;Password=test";

    for (var i = 0; i < 30; i++)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            return; // Ready
        }
        catch { await Task.Delay(1000); }
    }
    throw new TimeoutException("Postgres did not accept connections in 30s");
});

await resource.InitializeAsync();
```

**Seed data after startup:**

```csharp
resource.OnAfterReady(async _ =>
{
    await resource.ExecuteAsync("redis-cli SET test-key hello");
});
```

**Log resource state for debugging:**

```csharp
resource.OnBeforeDispose(async _ =>
{
    var logs = await resource.GetLogsAsync();
    Console.WriteLine($"Container logs:\n{logs}");
});
```

### Hooks with Each Framework

**xUnit (test base)** -- override `ConfigureContainer`, hooks are set in the
builder. For `OnAfterReady`, attach to the resource after init is not possible
with test bases (the resource is created internally). Use the builder's
built-in wait conditions instead, or use the concrete fixture / manual
`ResourceLifecycle` approach.

**MSTest** -- attach hooks in `[ClassInitialize]` before calling create:

```csharp
[ClassInitialize]
public static async Task ClassInit(TestContext ctx)
{
    (_kernel, _resource) = await ResourceLifecycle.CreateAndInitializeAsync(
        k =>
        {
            var r = new ContainerResource(k,
                b => b.UseImage("postgres:16")
                      .WithEnvironment("POSTGRES_PASSWORD=test")
                      .ExposePort("5432"));

            r.OnAfterReady(async _ =>
            {
                // Wait for Postgres to be connectable
                var ep = r.Container.ToHostExposedEndpoint("5432/tcp");
                // ... poll connection ...
            });

            return r;
        });
}
```

**NUnit** -- same pattern in `[OneTimeSetUp]`:

```csharp
[OneTimeSetUp]
public async Task Setup()
{
    (_kernel, _resource) = await ResourceLifecycle.CreateAndInitializeAsync(
        k =>
        {
            var r = new ContainerResource(k,
                b => b.UseImage("postgres:16")
                      .WithEnvironment("POSTGRES_PASSWORD=test")
                      .ExposePort("5432"));

            r.OnAfterReady(async _ => { /* readiness check */ });
            return r;
        });
}
```

## Diagnostics

When initialization fails, the `Diagnostics` property is populated with:
- `Failure` - the exception
- `Logs` - container/service logs (if `CaptureLogsOnFailure` is true), truncated
  to `MaxDiagnosticLogLines`
- `InspectPayload` - container inspect data as JSON
- `OperationContext` - additional context

## ResourceLifecycle (Advanced)

`ResourceLifecycle` is the shared static utility used by all framework adapters
(xUnit fixtures, MsTest helpers, NUnit helpers) to create, initialize, and
dispose resources. You only need to use it directly if you are building a custom
adapter or managing kernel lifetime yourself.

```csharp
// Create and initialize with default Docker CLI kernel
var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
    k => new ContainerResource(k, b => b.UseImage("redis:alpine")));

// Use resource...

// Dispose resource then kernel
await ResourceLifecycle.DisposeAsync(resource, kernel);
```

### Kernel Ownership

`CreateAndInitializeAsync` **takes ownership** of the kernel returned by the
factory. On success, the caller owns both the kernel and the resource and must
dispose them (typically via `DisposeAsync`). On initialization failure, both are
automatically cleaned up before the exception propagates.

> **Important:** The `kernelFactory` must return a **new** kernel each time it is
> called. Never return a shared or externally-managed kernel -- it will be disposed
> when the resource is torn down.

### Default Kernel Factories

| Factory | Creates |
|---|---|
| `CreateDefaultDockerKernelAsync()` | Docker CLI kernel (used when no factory is specified) |
| `CreateDefaultPodmanKernelAsync()` | Podman CLI kernel |

## Usage Example

```csharp
var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker-cli", d => d.AsDefault())
    .BuildAsync();

await using var resource = new ContainerResource(kernel, builder =>
    builder.UseImage("redis:alpine")
           .WithName("test-redis")
           .WaitForPort("6379/tcp"));

await resource.InitializeAsync();

// Use the container
var logs = await resource.GetLogsAsync();
```
