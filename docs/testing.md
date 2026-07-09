---
layout: default
title: Testing
nav_order: 9
has_children: true
---

# Test Support

FluentDocker v3 provides test support via the Testing.Core framework:

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> from [`feature/model-support`](https://github.com/mariotoffia/FluentDocker/tree/featrure/model-support) to use it. The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## Step by Step

- Basics: [Testing.Core (Recommended)](#testingcore-recommended), [Quick Examples](#quick-examples)
- Intermediate: [Detailed Documentation](#detailed-documentation), [Running by Category](#running-by-category)
- Advanced: [Standalone Kernel + Builder](#standalone-kernel--builder)

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

> **xUnit adapter is v3 only.** `FluentDocker.Testing.Xunit` targets xUnit v3 and
> is not compatible with xUnit v2 (`xunit` 2.x) projects.

## Quick Examples

### xUnit — Recommended Fixture Base

Use `XunitContainerFixtureBase` with `IClassFixture<T>` for integration suites:

```csharp
public class RedisFixture : XunitContainerFixtureBase
{
    protected override void ConfigureContainer(IContainerBuilder b) =>
        b.UseImage("redis:alpine").WaitForPort("6379/tcp");
}

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
}
```

### xUnit — Per-Test Base

Use `XunitContainerTestBase` only when each test method needs a fresh container:

```csharp
using FluentDocker.Testing.Xunit;

public class IsolatedRedisTests : XunitContainerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder b) =>
        b.UseImage("redis:alpine").WaitForPort("6379/tcp");

    [Fact]
    public async Task Redis_IsRunning()
    {
        var info = await Resource.InspectAsync();
        Assert.True(info.State.Running);
    }
}
```

### MSTest

```csharp
[TestClass]
public class RedisTests : MsTestContainerFixtureBase
{
    protected override void ConfigureContainer(IContainerBuilder b) =>
        b.UseImage("redis:alpine").WaitForPort("6379/tcp");

    [TestMethod]
    public async Task Redis_IsRunning()
    {
        var info = await Container.InspectAsync();
        Assert.IsTrue(info.State.Running);
    }
}
```

### NUnit

```csharp
[TestFixture]
public class RedisTests : NUnitContainerFixtureBase
{
    protected override void ConfigureContainer(IContainerBuilder b) =>
        b.UseImage("redis:alpine").WaitForPort("6379/tcp");

    [Test]
    public async Task Redis_IsRunning()
    {
        var info = await Container.InspectAsync();
        Assert.That(info.State.Running, Is.True);
    }
}
```

### Fixture lifetime comparison

| Adapter/base | Container lifetime |
|---|---|
| `XunitContainerTestBase` | One container per test method |
| `XunitContainerFixtureBase` | One container per xUnit class/collection fixture |
| `MsTestContainerFixtureBase` | One container per MSTest test method |
| `MsTestClassContainerFixtureBase<TFixture>` | One container shared by one MSTest test class |
| `NUnitContainerFixtureBase` | One container per NUnit fixture |

### Adapter parity

| Adapter | Fixture surface | Minimum runner |
|---|---|---|
| xUnit | Per-test base, class/collection fixture base, conditional fixture, and concrete resource fixtures | xUnit v3 |
| MSTest | Per-test base, class-level CRTP base, and helpers | MSTest 3.x |
| NUnit | One fixture base plus static helpers | NUnit 4.3.2+ |

## Detailed Documentation

| Topic | Description |
|---|---|
| [Core Types](testing/core.md) | Resource types, options, diagnostics, hooks, wait strategies |
| [xUnit Adapter](testing/xunit.md) | Test bases, fixture bases, concrete fixtures |
| [MSTest Adapter](testing/mstest.md) | Helper methods for all resource types |
| [NUnit Adapter](testing/nunit.md) | Helper methods for all resource types |
| [Docker Model Runner](testing/model.md) | Testing Docker Model Runner |
| [Migration from Legacy](testing/migration-from-legacy.md) | Side-by-side migration examples |
| [ADR 0001](adr/0001-testing-core-in-fluentdocker-package.md) | Why Testing.Core remains in the main package during preview |

## Running by Category

Tests use `[Trait("Category", "...")]` attributes. `make test` runs Unit;
`make test-integration` runs only `Integration` and `PodmanIntegration`;
use `dotnet test --filter "Category=X"` for one category. See
[Test Categories & Run Guide](testing/test-categories.md) for the full reference.

Use `make check` as the pre-push gate. It runs formatting, unit tests, adapter
runner tests through `make test-runners`, and coverage. Runner tests stay out of
coverage because they execute real MSTest/NUnit runners.

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
        var endpoint = await container.ToHostExposedEndpointAsync("80/tcp");
        using var client = new HttpClient();
        var response = await client.GetStringAsync(
            $"http://localhost:{endpoint.Port}");
        Assert.Contains("nginx", response);
    }
}
```

## Next Steps

[Core Types](testing/core.md) -- [Utilities](utilities.md) --
[Containers](containers.md) -- [Docker Compose](compose.md)
