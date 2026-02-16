# xUnit Adapter

Package: `FluentDocker.Testing.Xunit`

The xUnit adapter offers three patterns, from simplest to most flexible:

| Pattern | Lifecycle | Best for |
|---|---|---|
| **Test base** | Per-test (fresh container each test) | Isolated tests |
| **Fixture base** | Per-class or per-collection (shared) | Integration suites |
| **Concrete fixture** | Manual init (programmatic control) | Dynamic config |

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
         .WithProjectName("app-tests");

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
        b.UseNetwork(n => n.WithName("test-net"));
        b.UseContainer(c => c
            .UseImage("redis:alpine")
            .WithNetwork("test-net"));
        b.UseContainer(c => c
            .UseImage("nginx:alpine")
            .WithNetwork("test-net"));
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
            .WithPodmanCli("podman", d => d.AsDefault())
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
         .WithProjectName("integration");
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
runtime-computed parameters. These require explicit `InitializeAsync` calls.

### `XunitContainerFixture`

```csharp
public class DynamicFixture : XunitContainerFixture
{
    public DynamicFixture()
    {
        // Must use sync-over-async because xUnit constructors aren't async
        InitializeAsync(builder => builder
            .UseImage(Environment.GetEnvironmentVariable("TEST_IMAGE")
                ?? "redis:alpine")
            .WaitForPort("6379/tcp")
        ).GetAwaiter().GetResult();
    }
}
```

### `XunitResourceFixture<TResource>`

Generic fixture for any `ITestResource`, including plugin resources:

```csharp
public class CustomFixture : XunitResourceFixture<ContainerResource>
{
    public CustomFixture()
    {
        InitializeAsync(kernel =>
            new ContainerResource(kernel,
                c => c.UseImage("redis:alpine"))
        ).GetAwaiter().GetResult();
    }
}
```

### Other Concrete Fixtures

- `XunitComposeFixture` -- Docker Compose
- `XunitTopologyFixture` -- Multi-container topology
- `XunitSwarmStackFixture` -- Docker Swarm stacks
- `XunitPodmanKubernetesFixture` -- Podman `kube play`

All accept an optional `kernelFactory` and `DockerResourceOptions`.

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
            var endpoint = r.Container.ToHostExposedEndpoint("5432/tcp");
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
