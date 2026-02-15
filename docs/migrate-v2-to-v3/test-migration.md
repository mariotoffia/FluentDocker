---
layout: default
title: Test Migration
parent: Migration Guide
nav_order: 3
---

# Test Migration Guide

How to migrate FluentDocker v2.x.x test code to v3.0.0.

> **Note:** The legacy `FluentDocker.MsTest` and `FluentDocker.XUnit` packages
> have been removed. The examples below show the builder-level API changes.
> For test support, use the new `FluentDocker.Testing.*` packages. See
> [Migration from Legacy](../testing/migration-from-legacy.html) for
> side-by-side adapter examples.

This guide covers the most common test patterns and shows side-by-side v2 vs v3
code for each. The core change is that v3 requires a **kernel** with a registered
driver, the builder uses **lambda-scoped** configuration, and `Build()` returns
a `BuildResults` object instead of a service directly.

---

## 1. xUnit with IAsyncLifetime

The most common pattern: a test class that spins up a container before tests run
and tears it down afterward.

### v2

```csharp
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;
using Xunit;

public class PostgresTests : IAsyncLifetime
{
    private IContainerService _container;

    public async Task InitializeAsync()
    {
        _container = new Builder()
            .UseContainer()
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .ExposePort(5432)
            .WaitForPort("5432/tcp", 30000)
            .Build()
            .Start();
    }

    public Task DisposeAsync()
    {
        _container?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public void Container_IsRunning()
    {
        Assert.Equal(ServiceRunningState.Running, _container.State);
    }
}
```

### v3

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using Xunit;

public class PostgresTests : IAsyncLifetime
{
    private FluentDockerKernel _kernel;
    private BuildResults _results;
    private IContainerService _container;

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

        _container = _results.Containers.First();
    }

    public async Task DisposeAsync()
    {
        if (_results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (_kernel is IAsyncDisposable kd) await kd.DisposeAsync();
    }

    [Fact]
    public void Container_IsRunning()
    {
        Assert.Equal(ServiceRunningState.Running, _container.State);
    }
}
```

**What changed:**

- Three fields instead of one: `_kernel`, `_results`, `_container`.
- `FluentDockerKernel.Create()...BuildAsync()` creates the kernel with a DockerCli driver.
- Builder uses `.WithinDriver("docker", _kernel)` to scope to the registered driver.
- Container configuration is inside a lambda: `.UseContainer(c => c. ...)`.
- `Build()` no longer returns a service. Use `_results.Containers.First()`.
- No `.Start()` call -- `Build()` / `BuildAsync()` starts services automatically.
- `DisposeAsync` uses `IAsyncDisposable` pattern; dispose results first, then kernel.
- Port arguments are strings in v3 (e.g., `"5432"` not `5432`).

---

## 2. xUnit Collection Fixtures

Share one kernel and container across multiple test classes for speed. Multiple
kernels per application are supported when you need stronger isolation.

### v2

```csharp
// v2 -- shared fixture
public class SharedDatabaseFixture : IDisposable
{
    public IContainerService Container { get; }
    public string ConnectionString { get; }

    public SharedDatabaseFixture()
    {
        Container = new Builder()
            .UseContainer()
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .ExposePort(5432)
            .WaitForPort("5432/tcp", 30000)
            .Build()
            .Start();

        var ep = Container.ToHostExposedEndpoint("5432/tcp");
        ConnectionString =
            $"Host=localhost;Port={ep.Port};Database=postgres;" +
            "Username=postgres;Password=test";
    }

    public void Dispose() => Container?.Dispose();
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<SharedDatabaseFixture> { }

[Collection("Database")]
public class UserRepoTests
{
    private readonly SharedDatabaseFixture _db;
    public UserRepoTests(SharedDatabaseFixture db) => _db = db;

    [Fact]
    public void CanConnect() { /* use _db.ConnectionString */ }
}
```

### v3

```csharp
// v3 -- shared fixture with kernel
public class SharedDatabaseFixture : IAsyncLifetime
{
    public FluentDockerKernel Kernel { get; private set; }
    public IContainerService Container { get; private set; }
    public string ConnectionString { get; private set; }
    private BuildResults _results;

    public async Task InitializeAsync()
    {
        Kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        _results = await new Builder()
            .WithinDriver("docker", Kernel)
            .UseContainer(c => c
                .UseImage("postgres:15-alpine")
                .WithEnvironment("POSTGRES_PASSWORD=test")
                .ExposePort("5432")
                .WaitForPort("5432/tcp", 30000))
            .BuildAsync();

        Container = _results.Containers.First();
        var ep = Container.ToHostExposedEndpoint("5432/tcp");
        ConnectionString =
            $"Host=localhost;Port={ep.Port};Database=postgres;" +
            "Username=postgres;Password=test";
    }

    public async Task DisposeAsync()
    {
        if (_results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (Kernel is IAsyncDisposable kd) await kd.DisposeAsync();
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<SharedDatabaseFixture> { }

[Collection("Database")]
public class UserRepoTests
{
    private readonly SharedDatabaseFixture _db;
    public UserRepoTests(SharedDatabaseFixture db) => _db = db;

    [Fact]
    public async Task CanConnect()
    {
        // Use _db.ConnectionString -- same container for all tests in collection
    }
}

[Collection("Database")]
public class OrderRepoTests
{
    private readonly SharedDatabaseFixture _db;
    public OrderRepoTests(SharedDatabaseFixture db) => _db = db;

    [Fact]
    public async Task CanCreateOrder()
    {
        // Shares the same database container
    }
}
```

**What changed:**

- Fixture implements `IAsyncLifetime` instead of `IDisposable`.
- Kernel is created once in the fixture; all collection classes share it.
- `BuildResults` is stored for proper async disposal.

---

## 3. MSTest FluentDockerTestBase

The MSTest base class manages the kernel and container lifecycle for you.

### v2

```csharp
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.MsTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RedisTests : FluentDockerTestBase
{
    protected override ContainerBuilder Build()
    {
        return new Builder()
            .UseContainer()
            .UseImage("redis:alpine")
            .ExposePort(6379)
            .WaitForPort("6379/tcp", 30000);
    }

    [TestMethod]
    public void Container_IsRunning()
    {
        Assert.AreEqual(ServiceRunningState.Running, Container.State);
    }
}
```

### v3

```csharp
using FluentDocker.MsTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RedisTests : FluentDockerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("redis:alpine")
            .ExposePort("6379")
            .WaitForPort("6379/tcp", 30000);
    }

    [TestMethod]
    public void Container_IsRunning()
    {
        Assert.AreEqual(ServiceRunningState.Running, Container.State);
    }
}
```

**What changed:**

- Override `ConfigureContainer(IContainerBuilder builder)` instead of `Build()`.
- No return value -- configure the builder passed in.
- The base class creates and disposes the kernel automatically.
- No `new Builder()` or `.UseContainer()` -- the base class handles that.
- Ports are strings.

### Customising the Kernel (MSTest)

Override `CreateKernelAsync` when you need non-default configuration:

```csharp
[TestClass]
public class CustomKernelTests : FluentDockerTestBase
{
    protected override async Task<FluentDockerKernel> CreateKernelAsync()
    {
        return await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d
                .WithSudo(SudoMechanism.NoPassword)
                .AsDefault())
            .BuildAsync();
    }

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("nginx:alpine")
            .ExposePort("80")
            .WaitForPort("80/tcp", 30000);
    }
}
```

---

## 4. xUnit FluentDockerTestBase with IClassFixture

The xUnit base class follows the same `ConfigureContainer` pattern. Use
`IClassFixture<T>` to share a single container across all tests in the class.

### v2

```csharp
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.XUnit;
using Xunit;

public class NginxFixture : FluentDockerTestBase
{
    protected override ContainerBuilder Build()
    {
        return new Builder()
            .UseContainer()
            .UseImage("nginx:alpine")
            .ExposePort(80)
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

### v3

```csharp
using FluentDocker.XUnit;
using FluentDocker.Services.Extensions;
using Xunit;

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

    [Fact]
    public async Task Nginx_ReturnsWelcomePage()
    {
        var endpoint = _fixture.Container.ToHostExposedEndpoint("80/tcp");
        var client = new HttpClient();
        var response = await client.GetStringAsync(
            $"http://localhost:{endpoint.Port}");
        Assert.Contains("Welcome to nginx", response);
    }
}
```

**What changed:**

- Fixture overrides `ConfigureContainer` instead of `Build`.
- Namespace changes (`Ductus.FluentDocker.XUnit` to `FluentDocker.XUnit`).
- Extension methods like `ToHostExposedEndpoint` require `using FluentDocker.Services.Extensions`.

---

## 5. Compose in Tests

Multi-container stacks use `.UseCompose()` instead of `.UseContainer().UseCompose()`.

### v2

```csharp
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;
using Xunit;

public class ComposeTests : IAsyncLifetime
{
    private ICompositeService _svc;

    public async Task InitializeAsync()
    {
        _svc = new Builder()
            .UseContainer()
            .UseCompose()
            .FromFile("docker-compose.yml")
            .RemoveOrphans()
            .WaitForHttp("api", "http://localhost:8080/health")
            .Build()
            .Start();
    }

    public Task DisposeAsync()
    {
        _svc?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public void AllServicesRunning()
    {
        Assert.Equal(ServiceRunningState.Running, _svc.State);
    }
}
```

### v3

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using Xunit;

public class ComposeTests : IAsyncLifetime
{
    private FluentDockerKernel _kernel;
    private BuildResults _results;

    public async Task InitializeAsync()
    {
        _kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        _results = await new Builder()
            .WithinDriver("docker", _kernel)
            .UseCompose(c => c
                .WithComposeFile("docker-compose.yml")
                .WithRemoveOrphans()
                .WithWait()
                .WithWaitTimeout(60))
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        if (_results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (_kernel is IAsyncDisposable kd) await kd.DisposeAsync();
    }

    [Fact]
    public void AllContainersAreAccessible()
    {
        Assert.NotEmpty(_results.Containers);
    }

    [Fact]
    public async Task Api_RespondsToHealthCheck()
    {
        var api = _results.Containers
            .First(c => c.Name.Contains("api"));
        var endpoint = api.ToHostExposedEndpoint("8080/tcp");

        var client = new HttpClient();
        var response = await client.GetAsync(
            $"http://localhost:{endpoint.Port}/health");
        Assert.True(response.IsSuccessStatusCode);
    }
}
```

To share a compose stack across test classes, apply the same collection fixture
pattern from section 2 but use `.UseCompose()` instead of `.UseContainer()`.

**Key compose differences:**

| Aspect | v2 | v3 |
|--------|----|----|
| Entry point | `.UseContainer().UseCompose()` | `.UseCompose(c => ...)` |
| Compose file | `.FromFile("x.yml")` | `.WithComposeFile("x.yml")` |
| Orphan removal | `.RemoveOrphans()` | `.WithRemoveOrphans()` |
| Wait strategy | `.WaitForHttp("svc", "url")` | `.WithWait()` + compose healthchecks |
| Build result | `ICompositeService` | `BuildResults` |
| Access containers | `_svc.Containers` | `_results.Containers` |

---

## 6. Key Differences Summary

| Aspect | v2 | v3 |
|--------|----|----|
| Kernel | Not needed | Required: `FluentDockerKernel.Create().WithDockerCli(...)` |
| Build result type | `IContainerService` directly | `BuildResults` (access `.Containers.First()`) |
| Type for field | `IContainerService` / `ICompositeService` | `BuildResults` (concrete class) |
| Interface `IBuildResults` | Does not exist | Does not exist -- use `BuildResults` |
| Dispose | `IDisposable` | `IAsyncDisposable` preferred |
| Test base override | `ContainerBuilder Build()` | `void ConfigureContainer(IContainerBuilder)` |
| Start call | `.Build().Start()` | `.Build()` / `.BuildAsync()` auto-starts |
| Port args | `int` (e.g., `80`) | `string` (e.g., `"80"`) |
| Namespace | `Ductus.FluentDocker.*` | `FluentDocker.*` |
| Extension methods | Implicit | `using FluentDocker.Services.Extensions;` |
| Docker Machine | `new Hosts().Discover()` | Removed -- use Docker Contexts |
| Sudo config | `SudoMechanism.NoPassword.SetSudo()` | `.WithSudo(SudoMechanism.NoPassword)` in kernel builder |

---

## Common Migration Mistakes

1. **Forgetting async disposal order.** Always dispose `BuildResults` before the
   kernel. The results hold references to containers that need the kernel's
   driver to clean up.

2. **Using `IBuildResults` as a type.** There is no such interface. Use the
   concrete `BuildResults` class.

3. **Calling `.Start()` after `.Build()`.** In v3, `Build()` / `BuildAsync()`
   already starts the services. Calling `.Start()` again is harmless but
   unnecessary.

4. **Missing `using FluentDocker.Services.Extensions;`.** Extension methods like
   `ToHostExposedEndpoint` and `GetConfiguration` moved to this namespace.

5. **Using `container.Resume()`.** Renamed to `container.Start()` in v3.

6. **Using `container.Logs()`.** Renamed to `await container.GetLogsAsync()`.

---

## Next Steps

- [Migration Guide](../migration.html) -- full API migration reference
- [Testing](../testing.html) -- complete v3 test documentation
- [Docker Compose](../compose.html) -- compose patterns and examples
