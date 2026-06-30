# FluentDocker.Testing.Xunit

xUnit v3 test helpers for FluentDocker. Spin up containers, networks, volumes,
Compose stacks, and **Docker Model Runner** models (via `ModelResource`)
directly in your tests, with the full resource lifecycle managed as async
fixtures. This package requires xUnit v3 (`xunit.v3`) and is not compatible with
xUnit 2.x.

## Install

```bash
dotnet add package FluentDocker.Testing.Xunit
```

## Base-class fixture

```csharp
using FluentDocker.Builders;
using FluentDocker.Testing.Xunit;
using Xunit;

public sealed class RedisTests : XunitContainerTestBase
{
  protected override void ConfigureContainer(IContainerBuilder builder)
      => builder.UseImage("redis:7-alpine");

  [Fact]
  public async Task Redis_IsRunning()
  {
    var info = await Container.InspectAsync();
    Assert.Equal("running", info.State.Status);
  }
}
```

`Resource`, `Container`, and `Kernel` are non-null after xUnit initializes the fixture. Accessing them earlier throws `InvalidOperationException`.

## Concrete fixture

There is no helper class for xUnit. For programmatic control, subclass a
concrete fixture and call `Configure(...)` in the constructor, then share it
with `IClassFixture<T>`:

```csharp
using FluentDocker.Builders;
using FluentDocker.Testing.Xunit;
using Xunit;

public sealed class PostgresFixture : XunitContainerFixture
{
  public PostgresFixture()
      => Configure(c => c.UseImage("postgres:16-alpine"));
}

public sealed class PostgresTests : IClassFixture<PostgresFixture>
{
  private readonly PostgresFixture _f;
  public PostgresTests(PostgresFixture f) => _f = f;

  [Fact]
  public void Container_IsRunning() => Assert.NotNull(_f.Container);
}
```

xUnit drives `IAsyncLifetime`, so the fixture initializes and disposes
automatically.

For a custom resource type, subclass `XunitResourceFixture<TResource>` and
call `Configure(...)` the same way (`ModelResource` lives in
`FluentDocker.Testing.Core`):

```csharp
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using Xunit;

public sealed class ChatModelFixture : XunitResourceFixture<ModelResource>
{
  public ChatModelFixture()
      => Configure(k => new ModelResource(k, "ai/smollm2:latest",
          m => m.WithContextSize(4096)));
}

public sealed class ChatModelTests : IClassFixture<ChatModelFixture>
{
  private readonly ChatModelFixture _f;
  public ChatModelTests(ChatModelFixture f) => _f = f;

  [Fact]
  public void Model_IsLoaded() => Assert.NotNull(_f.Resource.Runner);
}
```

## Docker Model Runner end-to-end

A complete, nullable-enabled test that probes Docker Model Runner, skips
cleanly when it is not running (unless `FLUENTDOCKER_REQUIRE_DMR=1` forces a
hard fail), loads `ai/smollm2:latest` via `ModelResource`, and chats with it.
The fixture's `IAsyncLifetime.DisposeAsync` unloads the model and disposes the
kernel automatically.

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

  // Probe Docker Model Runner up front. Skip the suite when it is unavailable,
  // unless FLUENTDOCKER_REQUIRE_DMR=1 — then a missing runner hard-fails so a
  // broken DMR path cannot pass CI green with zero real coverage.
  private static async Task RequireRunnerAsync(CancellationToken ct)
  {
    await using var kernel = await ResourceLifecycle.CreateDefaultDockerKernelAsync();
    var driverId = kernel.DefaultDriverId;
    var runtime = kernel.SysCtl<IModelRuntimeDriver>(driverId);
    var status = await runtime.StatusAsync(new DriverContext(driverId), ct);
    var running = status.Success && status.Data.Running;

    if (running)
      return;

    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLUENTDOCKER_REQUIRE_DMR")))
      Assert.Skip("Docker Model Runner is not running.");

    throw new InvalidOperationException(
        "FLUENTDOCKER_REQUIRE_DMR=1 but Docker Model Runner is not running.");
  }

  [Fact]
  public async Task Smollm_Chats()
  {
    var ct = TestContext.Current.CancellationToken;
    await RequireRunnerAsync(ct);

    // resource.Model is the parsed ModelReference; resource.Runner / resource.Service
    // are the live handles. The fixture disposes (unloads) the model after the class.
    Assert.Equal("ai/smollm2:latest", _f.Resource.Model.ToString());

    var reply = await _f.Resource.Runner.ChatAsync("Reply with a single word.", ct);
    Assert.False(string.IsNullOrWhiteSpace(reply));
  }
}
```

> The fixture (`IClassFixture<>`) loads the model before tests run, so when the
> runner is down the *fixture* fails rather than reaching `Assert.Skip`. For a
> truly skip-or-hard-fail-only suite, probe first and create the resource
> manually (see the `docs/testing/xunit.md` model section). Tag DMR tests with
> `[Trait("Category","Integration")] [Trait("Requires","Dmr")]`.

Full docs: `docs/testing/xunit.md`. Model testing guide: `docs/testing/model.md`.
