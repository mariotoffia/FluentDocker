---
layout: default
title: Testing Core
parent: Testing
nav_order: 1
---

# FluentDocker Testing Core

The testing core lives inside the main `FluentDocker` assembly under the namespace
`FluentDocker.Testing.Core`. No separate NuGet package is needed.

**Packaging decision:** testing support ships in the production assembly so the
framework adapter packages stay thin and no fourth core package is needed. The
unused testing plugin host was removed before the preview API freeze, deleting
213 lines of public `FluentDocker.Testing.Core.Plugins` surface instead of
carrying dead API.

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
    MaxDiagnosticLogLines = 200,                // truncate logs beyond this
    TeardownTimeout = TimeSpan.FromSeconds(120) // Max time for teardown (default: 120s)
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

### Orphan Cleanup

Resources created by the testing core are tagged with the `fluentdocker.managed`
label. `CleanupOrphansOnInit` defaults to **true**, so `InitializeAsync` removes
managed resources left behind by earlier sessions. Set it to `false` to opt out:

```csharp
var options = new DockerResourceOptions
{
    CleanupOrphansOnInit = false // opt out; defaults to true
};
```

Orphan cleanup scans **containers, networks and volumes only**. Resources created
through Docker Compose, Swarm stacks, or Kubernetes YAML are not
removed by orphan cleanup unless they individually carry the `fluentdocker.managed`
label.

`EnableSessionLabels` is applied directly by `ContainerResource`, `NetworkResource`,
`VolumeResource`, and the top-level `UseContainer`/`UseNetwork`/`UseVolume` resources
of a `TopologyResource`. Containers that a Compose or pod operation spawns are **not**
session-labeled — those builders are not label-capable — so for Compose, Swarm,
Kubernetes, image pull, and model resources add labels in the underlying
compose/YAML/build definition when you need orphan cleanup to see them.

`OrphanCleanupMinimumAge` (a `TimeSpan`, default 1 hour) bounds what cleanup may
remove: only managed resources **older** than this age are deleted. With the
default, enabling `CleanupOrphansOnInit` in parallel CI cannot delete a sibling
test run's live resources, because those are younger than the threshold:

```csharp
var options = new DockerResourceOptions
{
    CleanupOrphansOnInit = true,
    OrphanCleanupMinimumAge = TimeSpan.FromHours(1) // default
};
```

Keep the default unless you run cleanup outside of parallel test execution.

### Cleaning up managed containers by hand

Every resource the testing core creates carries the `fluentdocker.managed=true`
label, so a CI job can reap leftovers without going through the framework:

```bash
docker ps -aq --filter label=fluentdocker.managed=true | xargs -r docker rm -f
```

On a **shared** daemon this cuts both ways: `CleanupOrphansOnInit` with the
default one-hour `OrphanCleanupMinimumAge` can reap a long-running,
framework-labeled container from a parallel run once it crosses the age
threshold. Give each CI job its own daemon, or raise `OrphanCleanupMinimumAge`
above your longest job when several runs share one daemon.

### Cleaning up leaked containers in CI

The testing core removes its containers when the fixture is disposed — that is,
during normal test teardown. When a CI runner sends `SIGKILL` (`kill -9`) — job
timeout, cancelled pipeline, agent teardown — the process dies before disposal runs,
so session-labeled containers stay up. The next run won't reclaim them either: the
default one-hour `OrphanCleanupMinimumAge` guard keeps `CleanupOrphansOnInit` from
touching resources younger than an hour, so freshly leaked containers survive until
they age past the threshold.

Reap them explicitly at the start (or end) of the job. Every managed resource carries
the `fluentdocker.managed=true` label:

```bash
docker ps -aq --filter label=fluentdocker.managed=true | xargs -r docker rm -f
```

Podman uses the same label:

```bash
podman ps -aq --filter label=fluentdocker.managed=true | xargs -r podman rm -f
```

Add this step when jobs run on ephemeral or shared CI agents, or anywhere a forced kill
can interrupt teardown. `xargs -r` skips the `rm` call when nothing matches, so the step
is a no-op on a clean daemon.

## Skipping when Docker is unavailable

`DockerAvailability.IsAvailableAsync` (in `FluentDocker.Testing.Core`) probes the
target runtime and returns `false` on availability failures (daemon down, binary missing,
internal timeout). It honors the caller's `CancellationToken` — a cancelled token
propagates `OperationCanceledException` rather than reporting "unavailable". Optional
`kernelFactory`/`driverId` arguments select a non-default runtime.

```csharp
using FluentDocker.Testing.Core;

// xUnit v3
Assert.SkipWhen(!await DockerAvailability.IsAvailableAsync(), "Docker not available");

// NUnit
if (!await DockerAvailability.IsAvailableAsync()) Assert.Ignore("Docker not available");

// MSTest
if (!await DockerAvailability.IsAvailableAsync()) Assert.Inconclusive("Docker not available");
```

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

Advanced HTTP wait with a full URL, custom method, and response handling. This is the
separate `WaitForHttpUrl` overload — the simple port+path `WaitForHttp` above still exists:

```csharp
builder.WaitForHttpUrl(
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
    if (attempt > 30) return -1; // -1 = ready (not "give up"); use timeoutMs to fail
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
    var endpoint = await resource.Container.ToHostExposedEndpointAsync("5432/tcp");
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
                var ep = await r.Container.ToHostExposedEndpointAsync("5432/tcp");
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

### Fixture lifetime by framework

The fixture base classes provision a container at different scopes. A suite ported
across frameworks without adjusting for this runs slower (or shares state) silently:

| Adapter | Base class | Provisioning scope | Hook |
|---|---|---|---|
| xUnit | `XunitContainerFixtureBase` | per class/collection fixture | `IAsyncLifetime` |
| MSTest | `MsTestContainerFixtureBase` | per **test method** | `[TestInitialize]`/`[TestCleanup]` |
| MSTest | `MsTestClassContainerFixtureBase<T>` | per class | guarded `[TestInitialize]`/`[ClassCleanup]` |
| NUnit | `NUnitContainerFixtureBase` | per class | `[OneTimeSetUp]`/`[OneTimeTearDown]` |

`MsTestContainerFixtureBase` starts a fresh container for every test method; use
`MsTestClassContainerFixtureBase<T>` for one container per class.

## Diagnostics

When initialization fails, FluentDocker throws `ResourceInitializationException`.
Its `Diagnostics` property remains reachable even when an adapter disposes the
failed resource and kernel. The resource's `Diagnostics` property is also
populated while the resource object is still in scope.

Diagnostics include:
- `Failure` - the exception
- `Logs` - container/service logs (if `CaptureLogsOnFailure` is true), truncated
  to `MaxDiagnosticLogLines`
- `InspectPayload` - container inspect data as JSON
- `OperationContext` - additional context
- `ResourceName` (string) - the name of the resource that failed
- `DriverId` (string) - the driver ID used by the resource

For compatibility, container readiness failures that expose
`ex.Data["ContainerLogTail"]` copy that value onto the thrown
`ResourceInitializationException`.

### When teardown fails

If graceful disposal fails, `DisposeAsync` records the failure in
`LastTeardownDiagnostics`. When `ForceRemoveOnDispose` is true, FluentDocker then
tries a fresh-token force remove. If force remove succeeds, disposal completes and
`LastTeardownDiagnostics.ForceRemoveException` is null. If force remove also
fails, `DisposeAsync` rethrows the graceful teardown exception and leaves the
resource handles available so you can inspect diagnostics or retry cleanup.

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

### Capturing framework logs

Framework and resource warnings are written through the **kernel's**
`ILoggerFactory`. The default kernels (`CreateDefaultDockerKernelAsync` /
`CreateDefaultPodmanKernelAsync`, used when no factory is specified) fall back to
`NullLoggerFactory.Instance`, so nothing is emitted. To capture the output, build
the kernel with a real `ILoggerFactory` — either override the fixture's
`KernelFactory` property or pass a `kernelFactory` to the helper:

```csharp
protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
    () => FluentDockerKernel.Create(myLoggerFactory) // your ILoggerFactory
        .WithDockerCli("docker-cli", d => d.AsDefault())
        .BuildAsync();
```

## Usage Example

```csharp
var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker-cli", d => d.AsDefault())
    .BuildAsync();

await using var resource = new ContainerResource(kernel, builder =>
    builder.UseImage("redis:alpine")
           .WithName($"test-redis-{Guid.NewGuid():N}") // unique — parallel-safe
           .WaitForPort("6379/tcp"));

await resource.InitializeAsync();

// Use the container
var logs = await resource.GetLogsAsync();
```
