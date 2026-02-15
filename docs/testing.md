---
layout: default
title: Testing
nav_order: 8
---

# Test Support

FluentDocker v3 provides test support via the Testing.Core framework:

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

> **Note:** `GetAwaiter().GetResult()` is used because xUnit does not
> support async fixture constructors. The fixture's `InitializeAsync`
> requires configuration parameters, which rules out the parameterless
> `IAsyncLifetime.InitializeAsync()`.

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

## Standalone Kernel + Builder

When you need full control, create a kernel and use the Builder directly
without any test base class:

```csharp
public class NginxTests : IAsyncLifetime
{
    private FluentDockerKernel _kernel;
    private BuildResults _results;

    public async ValueTask InitializeAsync()
    {
        _kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        _results = await new Builder()
            .WithinDriver("docker", _kernel)
            .UseContainer(c => c
                .UseImage("nginx:alpine")
                .ExposePort("80")
                .WaitForPort("80/tcp", 30000))
            .BuildAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (_kernel is IAsyncDisposable kd) await kd.DisposeAsync();
    }

    [Fact]
    public async Task Nginx_AcceptsConnections()
    {
        var container = _results.Containers.First();
        var endpoint = container.ToHostExposedEndpoint("80/tcp");
        using var client = new HttpClient();
        var response = await client.GetStringAsync(
            $"http://localhost:{endpoint.Port}");
        Assert.Contains("nginx", response);
    }
}
```

## Next Steps

[Core Types](testing/core.html) -- [Utilities](utilities.html) --
[Containers](containers.html) -- [Docker Compose](compose.html)
