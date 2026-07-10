---
layout: default
title: xUnit Adapter
parent: Testing
nav_order: 2
---

# xUnit Adapter

Package: `FluentDocker.Testing.Xunit`

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> from [`master`](https://github.com/mariotoffia/FluentDocker/tree/master) to use it. The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

> **xUnit v3 only.** This package depends on `xunit.v3.extensibility.core` and
> targets xUnit v3. It is not compatible with xUnit v2 (`xunit` 2.x) projects.

Recommended entry point: use `XunitContainerFixtureBase` (or another `Xunit*FixtureBase`)
with `IClassFixture<T>` for integration suites. Use `XunitContainerTestBase` only when each test method needs a fresh container.

The xUnit adapter offers three patterns:

| Pattern | Lifecycle | Best for |
|---|---|---|
| **Fixture base** | Per-class or per-collection (shared) | Integration suites |
| **Test base** | Per-test (fresh container each test) | Isolated tests |
| **Concrete fixture** | Manual init (programmatic control) | Dynamic config |

## Step by Step

- Setup: [Project setup / requirements](#project-setup--requirements)
- Basics: [Test Bases (Per-Test Lifecycle)](#test-bases-per-test-lifecycle), [Fixture Bases (Shared Lifecycle)](#fixture-bases-shared-lifecycle)
- Intermediate: [Collection Fixtures](#collection-fixtures), [Lifecycle Hooks with Wait Strategies](#lifecycle-hooks-with-wait-strategies)
- Advanced: [Concrete Fixtures (Advanced)](#concrete-fixtures-advanced), [Choosing the Right Pattern](#choosing-the-right-pattern)

## Project setup / requirements

`FluentDocker.Testing.Xunit` brings `xunit.v3.extensibility.core` transitively (fixture plumbing),
but consumers also need the v3 framework, VSTest runner, and test host. Requires **xUnit v3** (`xunit.v3`), not xUnit 2.x.

| Package | Why | Transitive from this package? |
| --- | --- | --- |
| `Microsoft.NET.Test.Sdk` | VSTest test host | No — add explicitly |
| `xunit.v3` | `[Fact]`/`[Theory]`, asserts, v3 framework | No — add explicitly |
| `xunit.runner.visualstudio` | Discovers/runs v3 tests under VSTest | No — add explicitly |

Minimal consumer `.csproj` (target `net10.0`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" PrivateAssets="all" />
    <PackageReference Include="FluentDocker.Testing.Xunit" Version="3.*" />
  </ItemGroup>
</Project>
```

> **Warning:** On a shared Docker or Podman daemon, the default
> `CleanupOrphansOnInit = true` can remove another session's eligible resources
> once they pass the one-hour `OrphanCleanupMinimumAge`: managed stopped
> containers and unused networks/volumes. Running containers and networks/volumes
> still in use are preserved. Set `FLUENTDOCKER_TEST_SESSION` for sibling
> processes, or opt out with `CleanupOrphansOnInit = false` from `GetOptions()`;
> see [Orphan Cleanup](core.md#orphan-cleanup).

## Test Bases (Per-Test Lifecycle)

Inherit from an abstract test base. xUnit calls `InitializeAsync` before
each test and `DisposeAsync` after. Override `ConfigureContainer` (or
`ConfigureCompose`, `ConfigureTopology`) to provide your setup.

### Container

```csharp
public class RedisTests : XunitContainerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder b) =>
        b.UseImage("redis:alpine")
         .WaitForPort("6379/tcp");

    [Fact]
    public async Task Redis_IsRunning()
    {
        var info = await Resource.InspectAsync();
        Assert.True(info.State.Running);
    }

    [Fact]
    public async Task Can_Read_Logs()
    {
        var logs = await Resource.GetLogsAsync();
        Assert.Contains("Ready to accept connections", logs);
    }
}
```

**Available properties:** `Resource` (ContainerResource), `Container`
(IContainerService), `Kernel` (FluentDockerKernel).

### Compose

```csharp
public class AppTests : XunitComposeTestBase
{
    protected override void ConfigureCompose(IComposeBuilder b) =>
        b.WithComposeFile("docker-compose.yml")
         .WithProjectName($"app-tests-{Guid.NewGuid():N}"); // unique — parallel-safe

    [Fact]
    public void Service_IsAvailable() => Assert.NotNull(Service);
}
```

### Topology

```csharp
public class MultiContainerTests : XunitTopologyTestBase
{
    protected override void ConfigureTopology(Builder b)
    {
        var net = $"test-net-{Guid.NewGuid():N}"; // unique — parallel-safe
        b.UseNetwork(n => n.WithName(net));
        b.UseContainer(c => c
            .UseImage("redis:alpine")
            .WithNetwork(net));
        b.UseContainer(c => c
            .UseImage("nginx:alpine")
            .WithNetwork(net));
    }

    [Fact]
    public void Both_Containers_Running() =>
        Assert.Equal(2, Resource.Containers.Count);
}
```

### Custom Options and Kernel

Override `GetOptions()` or `KernelFactory` to customize:

```csharp
public class PodmanRedisTests : XunitContainerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder b) =>
        b.UseImage("redis:alpine");

    protected override DockerResourceOptions GetOptions() => new()
    {
        Driver = DriverSelection.PodmanCli(),
        InitializationTimeout = TimeSpan.FromMinutes(5)
    };

    protected override Func<Task<FluentDockerKernel>> KernelFactory =>
        () => FluentDockerKernel.Create()
            .WithPodmanCli("podman-cli", d => d.AsDefault())
            .BuildAsync();
}
```

---

## Fixture Bases (Shared Lifecycle)

Inherit from an abstract fixture base and use it with `IClassFixture<T>` or
`ICollectionFixture<T>`. xUnit creates one instance and calls
`InitializeAsync` / `DisposeAsync` automatically via `IAsyncLifetime` -- no
sync-over-async `GetAwaiter().GetResult()` needed.

### Container Fixture

```csharp
// 1. Define the fixture
public class RedisFixture : XunitContainerFixtureBase
{
    protected override void ConfigureContainer(IContainerBuilder b) =>
        b.UseImage("redis:alpine").WaitForPort("6379/tcp");
}

// 2. Use it in tests (shared across all tests in this class)
public class RedisTests : IClassFixture<RedisFixture>
{
    private readonly RedisFixture _f;
    public RedisTests(RedisFixture f) => _f = f;

    [Fact]
    public async Task Redis_IsRunning()
    {
        var info = await _f.Resource.InspectAsync();
        Assert.True(info.State.Running);
    }

    [Fact]
    public async Task Redis_IsListening()
    {
        Assert.NotNull(_f.Container);
    }
}
```

### Compose Fixture

```csharp
public class AppFixture : XunitComposeFixtureBase
{
    protected override void ConfigureCompose(IComposeBuilder b) =>
        b.WithComposeFile("docker-compose.yml")
         .WithProjectName($"integration-{Guid.NewGuid():N}"); // unique — parallel-safe
}

public class AppTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _f;
    public AppTests(AppFixture f) => _f = f;

    [Fact]
    public void Service_IsAvailable() => Assert.NotNull(_f.Service);
}
```

### Topology Fixture

```csharp
public class StackFixture : XunitTopologyFixtureBase
{
    protected override void ConfigureTopology(Builder b)
    {
        b.UseContainer(c => c.UseImage("redis:alpine"));
        b.UseContainer(c => c.UseImage("nginx:alpine"));
    }
}

public class StackTests : IClassFixture<StackFixture>
{
    private readonly StackFixture _f;
    public StackTests(StackFixture f) => _f = f;

    [Fact]
    public void All_Containers_Running() =>
        Assert.Equal(2, _f.Resource.Containers.Count);
}
```

### Collection Fixtures

Share a fixture across multiple test classes by defining a collection:

```csharp
[CollectionDefinition("Redis")]
public class RedisCollection : ICollectionFixture<RedisFixture> { }

[Collection("Redis")]
public class RedisWriteTests
{
    private readonly RedisFixture _f;
    public RedisWriteTests(RedisFixture f) => _f = f;
    // Tests share the same container as RedisReadTests
}

[Collection("Redis")]
public class RedisReadTests
{
    private readonly RedisFixture _f;
    public RedisReadTests(RedisFixture f) => _f = f;
}
```

---

## Concrete Fixtures (Advanced)

Use concrete fixtures when you need programmatic control over
initialization -- e.g., dynamic configuration, conditional setup, or
runtime-computed parameters.

### Using `Configure` (Recommended)

Call `Configure(...)` in your constructor. xUnit calls `IAsyncLifetime`
automatically -- no sync-over-async needed:

```csharp
public class DynamicFixture : XunitContainerFixture
{
    public DynamicFixture()
    {
        Configure(builder => builder
            .UseImage(Environment.GetEnvironmentVariable("TEST_IMAGE")
                ?? "redis:alpine")
            .WaitForPort("6379/tcp"));
    }
}

public class RedisTests : IClassFixture<DynamicFixture>
{
    private readonly DynamicFixture _f;
    public RedisTests(DynamicFixture f) => _f = f;

    [Fact]
    public void Container_IsRunning() => Assert.NotNull(_f.Container);
}
```

### `XunitResourceFixture<TResource>`

Generic fixture for any `ITestResource`, including plugin resources and
`ModelResource` (Docker Model Runner). `ContainerResource`/`ModelResource` live
in `FluentDocker.Testing.Core`:

```csharp
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;

public class CustomFixture : XunitResourceFixture<ContainerResource>
{
    public CustomFixture()
    {
        Configure(kernel =>
            new ContainerResource(kernel,
                c => c.UseImage("redis:alpine")));
    }
}
```

### Manual `InitializeAsync` (Legacy)

For backward compatibility, you can still call `InitializeAsync` directly:

```csharp
public class ManualFixture : XunitContainerFixture
{
    public ManualFixture()
    {
        InitializeAsync(builder => builder
            .UseImage("redis:alpine")
        ).GetAwaiter().GetResult();
    }
}
```

### Other Concrete Fixtures

- `XunitComposeFixture` -- Docker Compose
- `XunitTopologyFixture` -- Multi-container topology
- `XunitSwarmStackFixture` -- Docker Swarm stacks
- `XunitPodmanKubernetesFixture` -- Podman `kube play`

All support `Configure(...)` with optional `kernelFactory` and
`DockerResourceOptions`.

---

## Docker Model Runner

`ModelResource` (in `FluentDocker.Testing.Core`) loads a Docker Model Runner
model for the duration of a test and unloads it on dispose. It exposes
`resource.Model` (the parsed `ModelReference`, readable before init),
`resource.Service` (`IModelService`), and `resource.Runner` (`IModelRunner` —
`ChatAsync`, `ChatStreamAsync`, `EmbedAsync`, …).

Because a `ModelResource` is just an `ITestResource`, drive it with
`XunitResourceFixture<ModelResource>` exactly like any other resource.

### Skip cleanly when the runner is down

The complete, nullable-enabled test below **probes Docker Model Runner first**,
creates the `ModelResource` only when it is running, and — to keep the skip
clean — manages the resource manually via `XunitResourceFixture<ModelResource>`
+ `await fixture.InitializeAsync(...)`. When the runner is absent it
`Assert.Skip`s, unless `FLUENTDOCKER_REQUIRE_DMR=1` forces a hard fail (so a
broken DMR path can't pass CI green with zero real coverage). The fixture's
`IAsyncLifetime.DisposeAsync` unloads the model and disposes the kernel.

```csharp
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using Xunit;

[Trait("Category", "Integration")]
[Trait("Requires", "Dmr")]
public sealed class SmolLmModelTests : IAsyncLifetime
{
    private readonly XunitResourceFixture<ModelResource> _fixture = new();
    private bool _skipped;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        // Probe DMR through its runtime port on a throwaway kernel.
        await using (var probe = await ResourceLifecycle.CreateDefaultDockerKernelAsync())
        {
            var driverId = probe.DefaultDriverId;
            var runtime = probe.SysCtl<IModelRuntimeDriver>(driverId);
            var status = await runtime.StatusAsync(new DriverContext(driverId), ct);
            var running = status.Success && status.Data.Running;

            if (!running)
            {
                // Required lanes hard-fail; PR/local lanes skip cleanly.
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLUENTDOCKER_REQUIRE_DMR")))
                {
                    _skipped = true;
                    return; // no resource created → DisposeAsync is a no-op
                }

                throw new InvalidOperationException(
                    "FLUENTDOCKER_REQUIRE_DMR=1 but Docker Model Runner is not running.");
            }
        }

        // Runner is up — load the model. Pin a context size to dodge the DMR
        // v1.2.1 auto-fit crash on chat models loaded without one.
        await _fixture.InitializeAsync(
            k => new ModelResource(k, "ai/smollm2:latest", m => m.WithContextSize(4096)),
            cancellationToken: ct);
    }

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Smollm_Chats()
    {
        Assert.SkipWhen(_skipped, "Docker Model Runner is not running.");
        var ct = TestContext.Current.CancellationToken;

        var resource = _fixture.Resource;
        Assert.Equal("ai/smollm2:latest", resource.Model.ToString());

        var reply = await resource.Runner.ChatAsync("Reply with a single word.", ct);
        Assert.False(string.IsNullOrWhiteSpace(reply));
    }

    [Fact]
    public async Task Smollm_Embeds()
    {
        Assert.SkipWhen(_skipped, "Docker Model Runner is not running.");
        var ct = TestContext.Current.CancellationToken;

        // Embeddings need an embedding model, not the chat default. Pull it once, then embed against it.
        var embedModel = ModelReference.Parse("ai/embeddinggemma");
        await _fixture.Resource.Runner.PullAsync(embedModel, cancellationToken: ct);
        var vector = await _fixture.Resource.Runner.EmbedAsync("hello world", embedModel, ct);
        Assert.NotEmpty(vector);
    }
}
```

### `IClassFixture` shorthand

When you don't need the conditional skip (e.g. a lane where the runner is
always present), the shorthand `IClassFixture<>` form is enough:

```csharp
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using Xunit;

public sealed class SmolLmFixture : XunitResourceFixture<ModelResource>
{
    public SmolLmFixture()
        => Configure(k => new ModelResource(k, "ai/smollm2:latest",
            m => m.WithContextSize(4096)));
}

public sealed class SmolLmTests : IClassFixture<SmolLmFixture>
{
    private readonly SmolLmFixture _f;
    public SmolLmTests(SmolLmFixture f) => _f = f;

    [Fact]
    public void Model_IsLoaded() => Assert.NotNull(_f.Resource.Runner);
}
```

> The `IClassFixture<>` form loads the model in the fixture *before* any test
> body runs, so a down runner fails the fixture rather than reaching
> `Assert.Skip`. Use the probe-first `IAsyncLifetime` pattern above when you
> need a clean skip.

---

## Lifecycle Hooks with Wait Strategies

Hooks on `ResourceBase` run at specific lifecycle points. The most common
use case is `OnAfterReady` for custom wait strategies beyond what the
builder's built-in wait conditions provide.

### Wait for a Health Check

```csharp
public class PostgresTests : XunitContainerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder b) =>
        b.UseImage("postgres:16")
         .WithEnvironment("POSTGRES_PASSWORD=test")
         .WaitForHealthy(60_000); // Uses Docker HEALTHCHECK
}
```

### Wait for a Log Message

```csharp
public class PostgresTests : XunitContainerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder b) =>
        b.UseImage("postgres:16")
         .WithEnvironment("POSTGRES_PASSWORD=test")
         .WaitForLogMessage("ready to accept connections", 60_000);
}
```

### Wait for an HTTP Endpoint

```csharp
public class ApiTests : XunitContainerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder b) =>
        b.UseImage("my-api:latest")
         .ExposePort("8080")
         .WaitForHttp("8080/tcp", "/health", 30_000);
}
```

### Custom Wait via OnAfterReady Hook

For wait strategies not covered by the built-in conditions (e.g., database
connectivity, custom protocol checks), use hooks on the resource directly.
Create the resource manually and attach hooks before initialization:

```csharp
// MSTest / NUnit style — works with any framework
var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
    k =>
    {
        var r = new ContainerResource(k,
            b => b.UseImage("postgres:16")
                  .WithEnvironment("POSTGRES_PASSWORD=test")
                  .ExposePort("5432"));

        r.OnAfterReady(async _ =>
        {
            // Poll until Postgres accepts connections
            var endpoint = await r.Container.ToHostExposedEndpointAsync("5432/tcp");
            var connStr = $"Host=localhost;Port={endpoint.Port};" +
                          "Username=postgres;Password=test";

            for (var i = 0; i < 30; i++)
            {
                try
                {
                    await using var conn = new NpgsqlConnection(connStr);
                    await conn.OpenAsync();
                    return; // Connected!
                }
                catch { await Task.Delay(1000); }
            }
            throw new TimeoutException("Postgres not ready after 30s");
        });

        return r;
    });
```

### All Lifecycle Hooks

```csharp
resource
    .OnBeforeInitialize(async r => { /* before preflight + provisioning */ })
    .OnAfterReady(async r =>        { /* resource is up — verify readiness */ })
    .OnBeforeDispose(async r =>     { /* before teardown — flush data, etc */ })
    .OnAfterDispose(async r =>      { /* after cleanup — log final state */ });
```

Hooks are chainable and run in registration order. Init-phase hooks that
throw will abort initialization with diagnostics captured. Dispose-phase
hooks are best-effort (exceptions are suppressed to ensure cleanup proceeds).

---

## Choosing the Right Pattern

| Need | Use |
|---|---|
| Fresh container per test | `XunitContainerTestBase` |
| Shared container for one test class | `XunitContainerFixtureBase` + `IClassFixture<T>` |
| Shared container across test classes | `XunitContainerFixtureBase` + `ICollectionFixture<T>` |
| Dynamic/runtime configuration | `XunitContainerFixture` (concrete) |
| Custom resource type or plugin | `XunitResourceFixture<TResource>` |
| Full kernel control | `IAsyncLifetime` + `FluentDockerKernel` directly |
