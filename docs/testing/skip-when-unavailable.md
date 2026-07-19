---
layout: default
title: Skip When Unavailable
parent: Testing
nav_order: 9
---

# Skip xUnit tests when Docker is unavailable

`XunitContainerFixtureBase.SkipWhenUnavailable` was removed — xUnit cannot turn a fixture
into a skip from `InitializeAsync`. Two pieces replace it.

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> it from source — see [Consume the preview](../getting-started.md#consume-the-preview). The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

**Preflight (fail fast).** Every `ITestResource` runs a runtime-health check before it
provisions. If the selected runtime is down, initialization throws
`ResourceInitializationException` whose `InnerException` is a
`FluentDockerUnavailableException` (from `FluentDocker.Common`, a `FluentDockerException`)
instead of a raw mid-provision error, so a fixture against a dead daemon fails fast with a
clear message. Match it with
`catch (ResourceInitializationException ex) when (ex.InnerException is FluentDockerUnavailableException)`.

**Graceful skip.** To skip rather than fail when Docker is absent, probe first and gate each
test with `Assert.SkipWhen`. `XunitContainerFixtureBase` exposes
`IsDockerAvailableAsync(CancellationToken)` for body-level checks; for setup code that runs
before any resource exists, call `DockerAvailability.IsAvailableAsync(...)` (in
`FluentDocker.Testing.Core`) — it returns `false` when the daemon is down, the binary is
missing, or the probe times out, and only propagates driver-resolution failures.

```csharp
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using Xunit;

[Trait("Category", "Integration")]
public sealed class RedisTests : IAsyncLifetime
{
    private readonly XunitResourceFixture<ContainerResource> _fixture = new();
    private bool _skipped;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        if (!await DockerAvailability.IsAvailableAsync(cancellationToken: ct))
        {
            _skipped = true;   // no resource created → DisposeAsync is a no-op
            return;
        }

        await _fixture.InitializeAsync(
            k => new ContainerResource(k, c => c.UseImage("redis:alpine").WaitForPort("6379/tcp")),
            cancellationToken: ct);
    }

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Redis_IsRunning()
    {
        Assert.SkipWhen(_skipped, "Docker is not available.");
        var info = await _fixture.Resource.InspectAsync();
        Assert.True(info.State.Running);
    }
}
```

For Podman, pass `driverId: "podman-cli"` and a Podman kernel factory to `IsAvailableAsync`.
The [Docker Model Runner fixtures](xunit.md#docker-model-runner) apply the same
probe-first shape to a DMR runtime probe.

## Built-in conditional fixture

`XunitConditionalContainerFixtureBase` (in `FluentDocker.Testing.Xunit`) packages the
probe-first flow: it owns its kernel, health-probes the driver during
`InitializeAsync`, and — when the runtime is down, the probe times out, or resource
init fails with a wrapped `FluentDockerUnavailableException` — sets `IsSkipped = true`
and `SkipReason` instead of throwing. Subclass it, override `ConfigureContainer`, register
it with `IClassFixture<T>`, and gate each test on `IsSkipped`:

```csharp
using FluentDocker.Builders;
using FluentDocker.Testing.Xunit;
using Xunit;

public sealed class RedisFixture : XunitConditionalContainerFixtureBase
{
    protected override void ConfigureContainer(IContainerBuilder builder) =>
        builder.UseImage("redis:alpine").WaitForPort("6379/tcp");
}

[Trait("Category", "Integration")]
public sealed class RedisTests : IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;
    public RedisTests(RedisFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Redis_IsRunning()
    {
        Assert.SkipWhen(_fixture.IsSkipped, _fixture.SkipReason);
        var info = await _fixture.Resource.InspectAsync();
        Assert.True(info.State.Running);
    }
}
```

Override `KernelFactory` (e.g. a Podman kernel) or `GetOptions` to change the runtime or
resource options. Access `Resource`, `Container`, or `Kernel` only after checking `IsSkipped`.

See also: **[xUnit Adapter](xunit.md)**.
