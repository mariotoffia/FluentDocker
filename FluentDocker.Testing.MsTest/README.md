# FluentDocker.Testing.MsTest

MSTest test helpers for FluentDocker. Spin up and tear down Docker / Podman
containers, networks, volumes, Compose stacks, and Docker Model Runner models
(via `ModelResource`) directly from MSTest lifecycle hooks, with the full
resource lifecycle managed for you.

## Install

```bash
dotnet add package FluentDocker.Testing.MsTest
```
> **Requires .NET 10.** All FluentDocker 3.2 packages (including this one) target
> `net10.0` only — a test project on .NET 8/9 LTS cannot reference them. Retarget the
> TEST project to `net10.0`; the production code under test can stay on its own TFM.


## Project setup / requirements

This package brings `MSTest.TestFramework` in transitively, so referencing
`FluentDocker.Testing.MsTest` gives you the `[TestClass]` / `[TestMethod]`
attribute set automatically. It does **not** make your project runnable on its
own — MSTest still needs the test host and the adapter to discover and run your
tests. A consumer test project needs:

- **`Microsoft.NET.Test.Sdk`** — the VSTest host (required to run any test project).
- **`MSTest.TestAdapter`** — discovers and executes MSTest `[TestClass]`es. **Not** transitive from this package; add it explicitly.
- **`MSTest.TestFramework`** — the attributes/asserts. Transitive via `FluentDocker.Testing.MsTest`, but add it explicitly if you want to pin the version.

Supported MSTest version: **3.7.3** (what this package references).

Minimal consumer `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.7.3" />
    <PackageReference Include="MSTest.TestFramework" Version="3.7.3" />
    <PackageReference Include="FluentDocker.Testing.MsTest" Version="3.*" />
  </ItemGroup>

</Project>
```

`<IsPackable>false</IsPackable>` is not needed for a consumer test project (it
just stops it being packed; harmless to omit). As an alternative to the three
test packages above, the modern **`MSTest.Sdk`** meta-package (set
`<Project Sdk="MSTest.Sdk/3.7.3">`) bundles the host, adapter, and framework in
one reference; then you only add `FluentDocker.Testing.MsTest`.

## Base-class fixture

Recommended entry point: use `MsTestPerTestContainerFixtureBase` for a fresh container
per test method. Use `MsTestClassContainerFixtureBase<TFixture>` only when a
single container must be shared by the whole test class.

> The old name `MsTestContainerFixtureBase` is now a deprecated alias for
> `MsTestPerTestContainerFixtureBase` (same behavior, clearer name). It still compiles
> with an obsolete warning; switch to the new name.

## Fixture lifetime (important)

| Helper | Container lifetime | Use when |
| --- | --- | --- |
| `MsTestPerTestContainerFixtureBase` | **Per test method** (`[TestInitialize]`/`[TestCleanup]`) | Tests must be isolated. |
| `MsTestClassContainerFixtureBase<TFixture>` | **Per test class** (lazy first `[TestInitialize]`, then `[ClassCleanup]`) | The class intentionally shares one expensive fixture. |

### ⚠️ Cross-package lifetime differs — do not assume by name

A base named `<Framework>ContainerFixtureBase` does **not** mean the same lifetime across packages.
`MsTestPerTestContainerFixtureBase` is per test **method**, whereas the like-named xUnit/NUnit bases are per
test **class** — migrating between frameworks can silently flip container isolation. Pick by the
lifetime column, not the class name.

| Package | `…ContainerFixtureBase` | Per-test-method (isolated) | Per-test-class (shared) |
| --- | --- | --- | --- |
| **xUnit** | per test **class** | `XunitContainerTestBase` | `XunitContainerFixtureBase` (`IClassFixture<T>`) |
| **NUnit** | per test **class** | subclass with `[SetUp]`/`[TearDown]` | `NUnitContainerFixtureBase` (`[OneTimeSetUp]`) |
| **MSTest** | per test **method** ⚠️ | `MsTestPerTestContainerFixtureBase` | `MsTestClassContainerFixtureBase<T>` |

## Best-effort crash cleanup

Normal cleanup runs during fixture disposal and the next initialization orphan sweep. Set
`FLUENTDOCKER_TEST_SESSION=<shared-id>` to group parallel test processes into one live session.
Process-exit/SIGINT/SIGTERM cleanup for the current session is **on by default**, so a Ctrl-C
or other catchable termination reaps this session's own labeled containers instead of leaking
them running. Set `FLUENTDOCKER_TEST_REAPER_ON_EXIT=0` (or `false`) to opt out. Shared
`FLUENTDOCKER_TEST_SESSION` runs skip exit reaping to avoid deleting sibling processes; SIGKILL
and hard CI termination cannot run in-process cleanup.

```csharp
using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Testing.MsTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class RedisTests : MsTestPerTestContainerFixtureBase
{
  protected override void ConfigureContainer(IContainerBuilder builder)
      => builder.UseImage("redis:7-alpine");

  [TestMethod]
  public async Task Redis_IsRunning()
  {
    var info = await Container.InspectAsync();
    Assert.AreEqual("running", info.State.Status);
  }
}
```

The base class creates one container per test method. `Resource`, `Container`, and `Kernel` are non-null after `TestInitialize`; earlier access throws `InvalidOperationException`.

## Class-scoped fixture base

`[ClassCleanup(ClassCleanupBehavior.EndOfClass)]` is mandatory. MSTest 3.x
defaults a bare `[ClassCleanup]` to end-of-assembly cleanup, which keeps the
static container and kernel alive until the whole test assembly finishes — pass
`ClassCleanupBehavior.EndOfClass` so teardown runs when the class ends. Use a
unique container name when you set one explicitly; fixed names collide under
parallel runs.

```csharp
using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Testing.MsTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class SharedRedisTests
    : MsTestClassContainerFixtureBase<SharedRedisTests>
{
  protected override void ConfigureContainer(IContainerBuilder builder)
      => builder
          .UseImage("redis:7-alpine")
          .WithName($"redis-tests-{Guid.NewGuid():N}");

  [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
  public static Task ClassCleanup() => CleanupClassAsync();

  [TestMethod]
  public async Task Redis_IsRunning()
  {
    var info = await Container.InspectAsync();
    Assert.AreEqual("running", info.State.Status);
  }
}
```

Pass the most-derived class as `TFixture`. If `B : A` reuses
`MsTestClassContainerFixtureBase<A>`, both classes share the same static
container and cleanup state.

## Class-scoped helper API

```csharp
private static FluentDockerKernel? _kernel;
private static ContainerResource? _resource;

[ClassInitialize]
public static async Task ClassInitialize(TestContext context)
{
  (_kernel, _resource) = await MsTestResourceHelpers.CreateContainerAsync(
      c => c.UseImage("postgres:16-alpine"));
}

[ClassCleanup(ClassCleanupBehavior.EndOfClass)]
public static async Task ClassCleanup()
    => await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
```

Full docs: [docs/testing/mstest.md](https://mariotoffia.github.io/FluentDocker/testing/mstest.html). Model testing guide:
[docs/testing/model.md](https://mariotoffia.github.io/FluentDocker/testing/model.html).

## Docker Model Runner (ModelResource)

`CreateResourceAsync` builds any `ITestResource`, including a `ModelResource`
that loads a Docker Model Runner model for the test and unloads it on cleanup.
Probe the runner first and skip cleanly when it is unavailable (unless
`FLUENTDOCKER_REQUIRE_DMR=1`, in which case the test hard-fails). Capture the
kernel and dispose both via `MsTestResourceHelpers.DisposeAsync`:

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
using FluentDocker.Testing.MsTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class SmolLmTests
{
  private static FluentDockerKernel? _kernel;
  private static ModelResource? _model;

  [ClassInitialize]
  public static async Task ClassInitialize(TestContext context)
  {
    var ct = context.CancellationTokenSource.Token;

    // Probe the runner with its own short-lived kernel.
    var probeKernel = await ResourceLifecycle.CreateDefaultDockerKernelAsync();
    bool running;
    try
    {
      var driverId = probeKernel.DefaultDriverId;
      var runtime = probeKernel.SysCtl<IModelRuntimeDriver>(driverId);
      var status = await runtime.StatusAsync(new DriverContext(driverId), ct);
      running = status.Success && status.Data.Running;
    }
    finally
    {
      await probeKernel.DisposeAsync();
    }

    if (!running)
    {
      var require = Environment.GetEnvironmentVariable("FLUENTDOCKER_REQUIRE_DMR");
      if (string.IsNullOrEmpty(require))
        Assert.Inconclusive("Docker Model Runner not available; skipping. " +
            "Set FLUENTDOCKER_REQUIRE_DMR=1 to require it.");
      throw new InvalidOperationException(
          "FLUENTDOCKER_REQUIRE_DMR=1 but Docker Model Runner is not running.");
    }

    (_kernel, _model) = await MsTestResourceHelpers.CreateResourceAsync<ModelResource>(
        k => new ModelResource(k, "ai/smollm2:latest"),
        cancellationToken: ct);
  }

  [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
  public static async Task ClassCleanup()
      => await MsTestResourceHelpers.DisposeAsync(_model, _kernel);

  [TestMethod]
  public async Task Chat_Responds()
  {
    Assert.IsNotNull(_model);
    var answer = await _model!.Runner.ChatAsync("Say hello in one word.");
    Assert.IsFalse(string.IsNullOrWhiteSpace(answer));
  }

  [TestMethod]
  public async Task Embeds_Vector()
  {
    Assert.IsNotNull(_model);
    // Embeddings need an embedding model, not the chat default. Pull it once, then embed against it.
    var embedModel = ModelReference.Parse("ai/embeddinggemma");
    await _model!.Runner.PullAsync(embedModel);
    var vector = await _model!.Runner.EmbedAsync("hello world", embedModel);
    Assert.IsTrue(vector.Count > 0);
  }
}
```

`Assert.Inconclusive` marks the class as skipped rather than failed when the
runner is missing. `_model.Runner` is the `IModelRunner` bound to the model;
`_model.Service` is the underlying `IModelService`; `_model.Model` is the
parsed `ModelReference`.
