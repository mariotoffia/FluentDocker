---
layout: default
title: Testing
nav_order: 8
---

# Test Support

FluentDocker v3 provides two generations of test support:

| Approach | Packages | Status |
|---|---|---|
| **Testing.Core** (recommended) | Built into `FluentDocker` + adapters | Current |
| **Legacy base classes** | `FluentDocker.MsTest`, `FluentDocker.XUnit` | Deprecated |

## Testing.Core (Recommended)

The testing core lives inside the main `FluentDocker` assembly under
`FluentDocker.Testing.Core`. It provides async resource types with
diagnostics, lifecycle hooks, and driver selection. Framework-specific
adapters are available as separate packages:

```bash
dotnet add package FluentDocker                    # Core (includes Testing.Core)
dotnet add package FluentDocker.Testing.Xunit      # xUnit adapter
dotnet add package FluentDocker.Testing.MsTest     # MSTest adapter
dotnet add package FluentDocker.Testing.NUnit      # NUnit adapter
```

### Quick Example (xUnit)

```csharp
using FluentDocker.Testing.Xunit;

public class MyFixture : XunitContainerFixture
{
    public MyFixture()
    {
        InitializeAsync(builder => builder
            .UseImage("redis:alpine")
            .WaitForPort("6379/tcp")
        ).GetAwaiter().GetResult();
    }
}

public class RedisTests : IClassFixture<MyFixture>
{
    private readonly MyFixture _fixture;
    public RedisTests(MyFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Redis_IsRunning()
    {
        var info = await _fixture.Container.InspectAsync();
        Assert.True(info.State.Running);
    }
}
```

### Quick Example (MSTest)

```csharp
using FluentDocker.Testing.MsTest;

[TestClass]
public class RedisTests
{
    private static FluentDockerKernel _kernel;
    private static ContainerResource _resource;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        (_kernel, _resource) = await MsTestResourceHelpers.CreateContainerAsync(
            builder => builder
                .UseImage("redis:alpine")
                .WaitForPort("6379/tcp"));
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
    }
}
```

### Quick Example (NUnit)

```csharp
using FluentDocker.Testing.NUnit;

[TestFixture]
public class RedisTests
{
    private FluentDockerKernel _kernel;
    private ContainerResource _resource;

    [OneTimeSetUp]
    public async Task Setup()
    {
        (_kernel, _resource) = await NUnitResourceHelpers.CreateContainerAsync(
            builder => builder
                .UseImage("redis:alpine")
                .WaitForPort("6379/tcp"));
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await NUnitResourceHelpers.DisposeAsync(_resource, _kernel);
    }
}
```

### Detailed Documentation

| Topic | Description |
|---|---|
| [Core Types](testing/core.html) | Resource types, options, diagnostics, hooks |
| [xUnit Adapter](testing/xunit.html) | Fixtures for container, compose, topology, swarm, podman |
| [MSTest Adapter](testing/mstest.html) | Helper methods for all resource types |
| [NUnit Adapter](testing/nunit.html) | Helper methods for all resource types |
| [Plugins](testing/plugins.html) | Extending resources with custom plugins |
| [Migration from Legacy](testing/migration-from-legacy.html) | Side-by-side migration examples |

## Running by Category

Tests use `[Trait("Category", "...")]` attributes (`make test` runs Unit,
`make test-integration` runs all, `dotnet test --filter "Category=X"` for a
single category). See [Test Categories & Run Guide](test-categories.html) for
the full reference.

---

## Legacy Packages (Deprecated)

The legacy packages `FluentDocker.MsTest` and `FluentDocker.XUnit` provide
base classes that manage a single container per test class. They remain
functional but are not recommended for new code.

```bash
dotnet add package FluentDocker.MsTest  # Legacy MSTest base classes
dotnet add package FluentDocker.XUnit   # Legacy xUnit base classes
```

### MSTest -- FluentDockerTestBase

```csharp
using FluentDocker.MsTest;

[TestClass]
public class NginxTests : FluentDockerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("nginx:alpine")
            .ExposePort("80")
            .WaitForPort("80/tcp", 30000);
    }

    [TestMethod]
    public void Container_IsRunning()
    {
        Assert.AreEqual(ServiceRunningState.Running, Container.State);
    }
}
```

### xUnit -- FluentDockerTestBase

```csharp
using FluentDocker.XUnit;

public class NginxFixture : FluentDockerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("nginx:alpine")
            .ExposePort("80")
            .WaitForPort("80/tcp", 30000);
    }
}

public class NginxTests : IClassFixture<NginxFixture>
{
    private readonly NginxFixture _fixture;
    public NginxTests(NginxFixture fixture) => _fixture = fixture;

    [Fact]
    public void Container_IsRunning()
    {
        Assert.Equal(ServiceRunningState.Running, _fixture.Container.State);
    }
}
```

See [Migration from Legacy](testing/migration-from-legacy.html) for detailed
migration examples from these packages to Testing.Core.

---

## Standalone Kernel + Builder

When you need full control, create a kernel and use the Builder directly
without any test base class:

```csharp
public class DatabaseTests : IAsyncLifetime
{
    private FluentDockerKernel _kernel;
    private BuildResults _results;
    private string _connectionString;

    public async Task InitializeAsync()
    {
        _kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        _results = await new Builder()
            .WithinDriver("docker", _kernel)
            .UseContainer(c => c
                .UseImage("postgres:15-alpine")
                .WithEnvironment("POSTGRES_PASSWORD=test")
                .ExposePort("5432")
                .WaitForPort("5432/tcp", 30000))
            .BuildAsync();

        var container = _results.Containers.First();
        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        _connectionString =
            $"Host=localhost;Port={endpoint.Port};Database=postgres;" +
            "Username=postgres;Password=test";
    }

    public async Task DisposeAsync()
    {
        if (_results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (_kernel is IAsyncDisposable kd) await kd.DisposeAsync();
    }

    [Fact]
    public async Task Database_AcceptsConnections()
    {
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        Assert.Equal(ConnectionState.Open, conn.State);
    }
}
```

## Next Steps

[Core Types](testing/core.html) -- [Utilities](utilities.html) --
[Containers](containers.html) -- [Docker Compose](compose.html)
