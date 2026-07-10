# FluentDocker.Testing.Xunit

xUnit v3 test helpers for FluentDocker. Spin up containers, networks, volumes,
Compose stacks, and **Docker Model Runner** models (via `ModelResource`)
directly in your tests, with the full resource lifecycle managed as async
fixtures. This package requires xUnit v3 (`xunit.v3`) and is not compatible with
xUnit 2.x.

## Best-effort crash cleanup

Normal cleanup runs during fixture disposal and the next initialization orphan sweep. Set
`FLUENTDOCKER_TEST_SESSION=<shared-id>` to group parallel test processes into one live session.
Set `FLUENTDOCKER_TEST_REAPER_ON_EXIT=1` to opt in to process-exit/SIGINT/SIGTERM cleanup for
the current session. Shared `FLUENTDOCKER_TEST_SESSION` runs skip exit reaping to avoid deleting
sibling processes; SIGKILL and hard CI termination cannot run in-process cleanup.

## Install

```bash
dotnet add package FluentDocker.Testing.Xunit
```

## Fixture base

Recommended entry point: use `XunitContainerFixtureBase` with `IClassFixture<T>`
for container integration suites. Use `XunitContainerTestBase` only when each
test method needs a fresh container.

## Fixture lifetime (important)

| Helper | Container lifetime | Use when |
| --- | --- | --- |
| `XunitContainerTestBase` | **Per test method** (xUnit creates a test class instance per method) | Tests must be isolated. |
| `XunitContainerFixtureBase` / `XunitContainerFixture` | **Per test class** with `IClassFixture<T>`; **per collection** with `ICollectionFixture<T>` | Tests intentionally share one expensive fixture. |

### ⚠️ Cross-package lifetime differs — do not assume by name

A base named `<Framework>ContainerFixtureBase` does **not** mean the same lifetime across packages.
Migrating between frameworks can silently flip container isolation — pick by the lifetime column,
not the class name.

| Package | `…ContainerFixtureBase` | Per-test-method (isolated) | Per-test-class (shared) |
| --- | --- | --- | --- |
| **xUnit** | per test **class** | `XunitContainerTestBase` | `XunitContainerFixtureBase` (`IClassFixture<T>`) |
| **NUnit** | per test **class** | subclass with `[SetUp]`/`[TearDown]` | `NUnitContainerFixtureBase` (`[OneTimeSetUp]`) |
| **MSTest** | per test **method** ⚠️ | `MsTestContainerFixtureBase` | `MsTestClassContainerFixtureBase<T>` |

MSTest's `MsTestContainerFixtureBase` is per **method** — the opposite of the like-named xUnit/NUnit
bases (per class).

```csharp
using FluentDocker.Builders;
using FluentDocker.Testing.Xunit;
using Xunit;

public sealed class RedisFixture : XunitContainerFixtureBase
{
  protected override void ConfigureContainer(IContainerBuilder builder)
      => builder.UseImage("redis:7-alpine");
}

public sealed class RedisTests : IClassFixture<RedisFixture>
{
  private readonly RedisFixture _fixture;

  public RedisTests(RedisFixture fixture) => _fixture = fixture;

  [Fact]
  public async Task Redis_IsRunning()
  {
    var info = await _fixture.Container.InspectAsync();
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

`IClassFixture<>` initializes the fixture *before* the test body runs, so it
cannot reach `Assert.Skip` when the runner is down — the fixture fails first. For
a suite that skips cleanly (or hard-fails under `FLUENTDOCKER_REQUIRE_DMR=1`),
drive `XunitResourceFixture<ModelResource>` manually: probe Docker Model Runner in
`InitializeAsync`, skip *before* constructing the resource, and only call
`fixture.InitializeAsync(...)` once the runner answers. The fixture's `DisposeAsync`
unloads the model and disposes the kernel.

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

        // Probe DMR through its runtime port on a throwaway kernel, BEFORE the
        // resource is created — so an absent runner skips instead of failing.
        await using (var probe = await ResourceLifecycle.CreateDefaultDockerKernelAsync())
        {
            var driverId = probe.DefaultDriverId;
            var runtime = probe.SysCtl<IModelRuntimeDriver>(driverId);
            var status = await runtime.StatusAsync(new DriverContext(driverId), ct);

            if (!(status.Success && status.Data.Running))
            {
                // PR/local lanes skip; must-run lanes set FLUENTDOCKER_REQUIRE_DMR=1 → hard-fail.
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

        // resource.Model is the parsed ModelReference; resource.Runner / resource.Service
        // are the live handles. The fixture unloads the model after the class.
        Assert.Equal("ai/smollm2:latest", _fixture.Resource.Model.ToString());

        var reply = await _fixture.Resource.Runner.ChatAsync("Reply with a single word.", ct);
        Assert.False(string.IsNullOrWhiteSpace(reply));
    }
}
```

> When the runner is always present (e.g. a dedicated CI lane), the
> `IClassFixture<>` shorthand shown earlier is enough — it just cannot skip
> cleanly. Tag DMR tests with
> `[Trait("Category","Integration")] [Trait("Requires","Dmr")]`.

Full docs: [docs/testing/xunit.md](https://mariotoffia.github.io/FluentDocker/testing/xunit.html). Model testing guide:
[docs/testing/model.md](https://mariotoffia.github.io/FluentDocker/testing/model.html).
