# FluentDocker Testing Core

The testing core lives inside the main `FluentDocker` assembly under the namespace
`FluentDocker.Testing.Core`. No separate NuGet package is needed.

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

## Lifecycle Hooks

All resources support four lifecycle hooks. The resource instance is passed to
each hook callback:

```csharp
resource
    .OnBeforeInitialize(async r => { /* before preflight + provisioning */ })
    .OnAfterReady(async r => { /* after resource is fully initialized */ })
    .OnBeforeDispose(async r => { /* before teardown starts */ })
    .OnAfterDispose(async r => { /* after cleanup completes */ });
```

## Diagnostics

When initialization fails, the `Diagnostics` property is populated with:
- `Failure` - the exception
- `Logs` - container/service logs (if `CaptureLogsOnFailure` is true), truncated
  to `MaxDiagnosticLogLines`
- `InspectPayload` - container inspect data
- `OperationContext` - additional context

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
