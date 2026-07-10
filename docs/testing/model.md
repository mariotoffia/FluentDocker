---
layout: default
title: Model Runner Testing
parent: Testing
nav_order: 5
---

# Testing Docker Model Runner

`ModelResource` gives a single **Docker Model Runner** model (an LLM or an
embeddings model) the same async test-resource lifecycle as a container:
initialize loads the model, dispose unloads it (unless you opt into
`KeepRunning()`). You drive inference through `resource.Runner` (chat,
streaming chat, embeddings) and the lifecycle through `resource.Service`.

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> it from source — see [Consume the preview](https://mariotoffia.github.io/FluentDocker/getting-started.html#consume-the-preview). The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

There is intentionally **no model-specific fixture family**. `ModelResource`
is created through the same generic resource path as every other resource —
`XunitResourceFixture<ModelResource>`, `NUnitResourceHelpers.CreateResourceAsync`,
`MsTestResourceHelpers.CreateResourceAsync`. That generic path is the supported
API; this guide shows the complete, copy-paste version for each framework.

## Step by Step

- Start here: [The one path (6 steps)](#the-one-path-6-steps)
- The #1 thing people get wrong: [Probe DMR and skip cleanly](#probe-dmr-and-skip-cleanly)
- Complete examples: [xUnit](#complete-xunit-example) · [NUnit](#complete-nunit-example) · [MSTest](#complete-mstest-example)
- Reference: [The runner surface](#the-runner-surface) · [Model references and pulls](#model-references-and-pulls) · [Cleanup](#cleanup)

## The one path (6 steps)

1. **Create a kernel** (Docker CLI):

   ```csharp
   var kernel = await ResourceLifecycle.CreateDefaultDockerKernelAsync();
   // registers the driver under id "docker-cli" and marks it default.
   ```

   Or build one yourself when you want a specific driver id (the integration
   suite uses `"docker"`):

   ```csharp
   var kernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
       .WithDockerCli("docker", d => d.AsDefault())
       .BuildAsync();
   ```

   > **Driver id must match.** The probe in step 2 and the `ModelResource` both
   > resolve ports by driver id. Use the **same id** you registered:
   > `"docker-cli"` for the default helper kernel, `"docker"` (or whatever you
   > chose) for a hand-built one. Mismatches throw `DriverNotFoundException`.

2. **Probe Docker Model Runner** — is it actually running on this host?

   ```csharp
   // kernel.DefaultDriverId is whatever you registered ("docker-cli" for the
   // helper kernel above, "docker" for the hand-built one) — using it keeps the
   // probe correct regardless of which kernel you created.
   var driverId = kernel.DefaultDriverId;
   var runtime = kernel.SysCtl<IModelRuntimeDriver>(driverId);
   var status = await runtime.StatusAsync(new DriverContext(driverId), cancellationToken);
   bool dmrRunning = status.Success && status.Data.Running;
   ```

3. **Skip cleanly** unless `FLUENTDOCKER_REQUIRE_DMR=1` is set, so PRs and
   local runs self-skip while must-run CI lanes hard-fail (see
   [Probe DMR and skip cleanly](#probe-dmr-and-skip-cleanly)).

4. **Create a `ModelResource`** via the generic resource path:

   ```csharp
   // xUnit fixture
   Configure(k => new ModelResource(k, "ai/smollm2:latest"));

   // NUnit / MSTest helper
   await NUnitResourceHelpers.CreateResourceAsync<ModelResource>(
       k => new ModelResource(k, "ai/smollm2:latest"));
   ```

5. **Use the model.** `resource.Runner` is an `IModelRunner`
   (`ChatAsync`, `ChatStreamAsync`, `EmbedAsync`, `ListAsync`, …);
   `resource.Service` is an `IModelService` (lifecycle handle);
   `resource.Model` is the `ModelReference`.

6. **Dispose through the framework helper** (null-safe): the xUnit fixture
   disposes automatically via `IAsyncLifetime`; NUnit/MSTest call
   `NUnitResourceHelpers.DisposeAsync(resource, kernel)` /
   `MsTestResourceHelpers.DisposeAsync(resource, kernel)`. Both `resource` and
   `kernel` may be `null` — no null-forgiving operator needed.

### Required usings for a model test

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;            // IModelRuntimeDriver
using FluentDocker.Kernel;             // FluentDockerKernel
using FluentDocker.Model.Drivers;      // DriverContext
using FluentDocker.Model.Models;       // ModelReference
using FluentDocker.Testing.Core;       // ModelResource, ResourceLifecycle, DockerResourceOptions
using Microsoft.Extensions.Logging.Abstractions; // NullLoggerFactory
// plus your framework + adapter:
//   xUnit  -> using Xunit;  using FluentDocker.Testing.Xunit;
//   NUnit  -> using NUnit.Framework;  using FluentDocker.Testing.NUnit;
//   MSTest -> using Microsoft.VisualStudio.TestTools.UnitTesting;  using FluentDocker.Testing.MsTest;
```

## Probe DMR and skip cleanly

This is the single most common mistake in model tests: running inference
without first checking that DMR is up, so the suite fails with an opaque
connection error on any machine where the runner isn't installed.

The pattern, mirrored from `ModelRunnerIntegrationTests`:

```csharp
// FLUENTDOCKER_REQUIRE_DMR=1 on must-run CI lanes: a missing/broken DMR HARD-FAILS
// (so a broken model code path can't pass green with zero coverage). Unset on PRs and
// locally: the test self-skips.
static bool RequireDmr =>
    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLUENTDOCKER_REQUIRE_DMR"));

static async Task<bool> IsDmrRunningAsync(FluentDockerKernel kernel, string driverId, CancellationToken ct)
{
    try
    {
        var runtime = kernel.SysCtl<IModelRuntimeDriver>(driverId);
        var status = await runtime.StatusAsync(new DriverContext(driverId), ct);
        return status.Success && status.Data.Running;
    }
    catch
    {
        return false; // DMR (or Docker) not reachable
    }
}
```

Then turn "not running" into a skip or a failure with the per-framework idiom:

| Framework | Skip (DMR optional) | Fail (`FLUENTDOCKER_REQUIRE_DMR=1`) |
|---|---|---|
| xUnit v3 | `throw new InvalidOperationException("$XunitDynamicSkip$Docker Model Runner is not running");` | `throw new InvalidOperationException("DMR required but not running");` |
| NUnit | `Assert.Ignore("Docker Model Runner is not running");` | `Assert.Fail("DMR required but not running");` |
| MSTest | `Assert.Inconclusive("Docker Model Runner is not running");` | `Assert.Fail("DMR required but not running");` |

A reusable gate that picks skip-or-fail for you (xUnit shown):

```csharp
static void SkipOrFailIfDmrDown(bool running)
{
    if (running)
        return;
    throw RequireDmr
        ? new InvalidOperationException("DMR required but not running")
        : new InvalidOperationException("$XunitDynamicSkip$Docker Model Runner is not running");
}
```

## Complete xUnit example

A fixture that builds a `"docker-cli"` kernel, the test class probes DMR once and
either skips or runs inference. The fixture's `IAsyncLifetime` disposes the
model and kernel automatically. Compiles with nullable enabled.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class ChatModelFixture : XunitResourceFixture<ModelResource>
{
    public const string DriverId = "docker-cli";

    public ChatModelFixture()
    {
        // The resource is built on the SAME kernel the kernelFactory returns,
        // so the resource resolves ports under DriverId too.
        Configure(
            k => new ModelResource(
                k,
                Environment.GetEnvironmentVariable("FLUENTDOCKER_DMR_CHAT_MODEL")
                    ?? "ai/smollm2:latest",
                // Pin a context size: DMR v1.2.1's bundled llama.cpp can crash when a
                // chat model loads without one. See "Model references and pulls" below.
                m => m.WithContextSize(4096),
                new DockerResourceOptions { InitializationTimeout = TimeSpan.FromMinutes(10) }),
            kernelFactory: () => FluentDockerKernel.Create(NullLoggerFactory.Instance)
                .WithDockerCli(DriverId, d => d.AsDefault())
                .BuildAsync());
    }
}

[Trait("Category", "Integration")]
[Trait("Requires", "Dmr")]
public sealed class ChatModelTests : IClassFixture<ChatModelFixture>
{
    private readonly ChatModelFixture _fixture;

    public ChatModelTests(ChatModelFixture fixture) => _fixture = fixture;

    private static bool RequireDmr =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLUENTDOCKER_REQUIRE_DMR"));

    private async Task SkipUnlessDmrAsync(CancellationToken ct)
    {
        bool running;
        try
        {
            var runtime = _fixture.Kernel.SysCtl<IModelRuntimeDriver>(ChatModelFixture.DriverId);
            var status = await runtime.StatusAsync(new DriverContext(ChatModelFixture.DriverId), ct);
            running = status.Success && status.Data.Running;
        }
        catch
        {
            running = false;
        }

        if (running)
            return;
        throw RequireDmr
            ? new InvalidOperationException("DMR required but not running")
            : new InvalidOperationException("$XunitDynamicSkip$Docker Model Runner is not running");
    }

    [Fact]
    public async Task Chat_ReturnsNonEmptyReply()
    {
        var ct = TestContext.Current.CancellationToken;
        await SkipUnlessDmrAsync(ct);

        var reply = await _fixture.Resource.Runner.ChatAsync("Reply with a single word.", ct);

        Assert.False(string.IsNullOrWhiteSpace(reply));
    }
}
```

> **Probing a fixture-owned kernel.** `XunitResourceFixture` runs the
> `kernelFactory` and `InitializeAsync` *before* your test body, so the model
> has already loaded by the time you can probe. If you want to skip *without*
> paying the model-load cost, gate at the `[Collection]`/assembly level instead:
> build a throwaway kernel, probe it, and self-skip before any fixture is
> created. For most suites the per-test probe above is enough.

## Complete NUnit example

Create the resource in `[OneTimeSetUp]`, capture both kernel and resource, and
dispose both in `[OneTimeTearDown]` with the null-safe helper.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

[TestFixture]
[Category("Integration")]
public sealed class NUnitModelTests
{
    private const string DriverId = "docker-cli";

    private FluentDockerKernel? _kernel;
    private ModelResource? _resource;

    private static bool RequireDmr =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLUENTDOCKER_REQUIRE_DMR"));

    [OneTimeSetUp]
    public async Task SetUp()
    {
        // Probe on a throwaway kernel first so we skip BEFORE loading a model.
        await using (var probe = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
            .WithDockerCli(DriverId, d => d.AsDefault()).BuildAsync())
        {
            if (!await IsDmrRunningAsync(probe, CancellationToken.None))
            {
                if (RequireDmr)
                    Assert.Fail("DMR required but not running");
                else
                    Assert.Ignore("Docker Model Runner is not running");
            }
        }

        (_kernel, _resource) = await NUnitResourceHelpers.CreateResourceAsync<ModelResource>(
            k => new ModelResource(k, "ai/smollm2:latest", m => m.WithContextSize(4096)),
            kernelFactory: () => FluentDockerKernel.Create(NullLoggerFactory.Instance)
                .WithDockerCli(DriverId, d => d.AsDefault())
                .BuildAsync());
    }

    [OneTimeTearDown]
    public Task TearDown() => NUnitResourceHelpers.DisposeAsync(_resource, _kernel);

    [Test]
    public async Task Chat_ReturnsNonEmptyReply()
    {
        var reply = await _resource!.Runner.ChatAsync("Reply with a single word.", CancellationToken.None);
        Assert.That(reply, Is.Not.Empty);
    }

    private static async Task<bool> IsDmrRunningAsync(FluentDockerKernel kernel, CancellationToken ct)
    {
        try
        {
            var runtime = kernel.SysCtl<IModelRuntimeDriver>(DriverId);
            var status = await runtime.StatusAsync(new DriverContext(DriverId), ct);
            return status.Success && status.Data.Running;
        }
        catch
        {
            return false;
        }
    }
}
```

## Complete MSTest example

Same shape, MSTest idioms — but **class-scoped**. `[ClassInitialize]` loads the
model once for the whole class; `[ClassCleanup(ClassCleanupBehavior.EndOfClass)]`
disposes it when the class ends. Model loads are expensive, so amortize one load
across every test method rather than reloading per test with `[TestInitialize]`;
use per-test setup only when a test must not observe another test's model state.
`ClassCleanupBehavior.EndOfClass` is required — a bare `[ClassCleanup]` runs at
end-of-assembly on MSTest 3.x and keeps the model loaded until the run ends.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.MsTest;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class MsTestModelTests
{
    private const string DriverId = "docker-cli";

    private static FluentDockerKernel? _kernel;
    private static ModelResource? _resource;

    private static bool RequireDmr =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLUENTDOCKER_REQUIRE_DMR"));

    [ClassInitialize]
    public static async Task Init(TestContext context)
    {
        var ct = context.CancellationTokenSource.Token;

        await using (var probe = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
            .WithDockerCli(DriverId, d => d.AsDefault()).BuildAsync())
        {
            if (!await IsDmrRunningAsync(probe, ct))
            {
                if (RequireDmr)
                    Assert.Fail("DMR required but not running");
                else
                    Assert.Inconclusive("Docker Model Runner is not running");
            }
        }

        (_kernel, _resource) = await MsTestResourceHelpers.CreateResourceAsync<ModelResource>(
            k => new ModelResource(k, "ai/smollm2:latest", m => m.WithContextSize(4096)),
            kernelFactory: () => FluentDockerKernel.Create(NullLoggerFactory.Instance)
                .WithDockerCli(DriverId, d => d.AsDefault())
                .BuildAsync());
    }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static Task Cleanup() => MsTestResourceHelpers.DisposeAsync(_resource, _kernel);

    [TestMethod]
    public async Task Chat_ReturnsNonEmptyReply()
    {
        var reply = await _resource!.Runner.ChatAsync("Reply with a single word.", CancellationToken.None);
        Assert.IsFalse(string.IsNullOrWhiteSpace(reply));
    }

    private static async Task<bool> IsDmrRunningAsync(FluentDockerKernel kernel, CancellationToken ct)
    {
        try
        {
            var runtime = kernel.SysCtl<IModelRuntimeDriver>(DriverId);
            var status = await runtime.StatusAsync(new DriverContext(DriverId), ct);
            return status.Success && status.Data.Running;
        }
        catch
        {
            return false;
        }
    }
}
```

## The runner surface

`resource.Runner` is an `IModelRunner` — a façade composing the store, engine,
and inference ports plus default-model convenience helpers. The most useful
members for tests:

| Member | From | What it does |
|---|---|---|
| `ChatAsync(prompt, ct)` | facade | One-shot chat against the default model; returns `string`. |
| `ChatStreamAsync(prompt, ct)` | facade | `IAsyncEnumerable<string>` — token-by-token deltas. |
| `EmbedAsync(text, model?, ct)` | facade | `IReadOnlyList<float>` embedding vector. |
| `ListAsync(ct)` | store | Models present in the local store. |
| `InspectAsync(model, ct)` | store | `ModelInfo` (format, size, …). |
| `ListRunningAsync(ct)` | engine | Currently loaded/served models. |
| `LoadAsync` / `UnloadAsync` | engine | Manual load/unload. |
| `StatusAsync(ct)` / `VersionAsync(ct)` | engine | Runner status / version. |
| `ChatCompletionAsync(request, ct)` | inference | Full OpenAI-style request with `Usage`. |
| `EmbeddingsAsync(request, ct)` | inference | Batch embeddings, OpenAI-style. |

`resource.Service` is an `IModelService` lifecycle handle (`StartAsync`,
`StopAsync`, `InspectAsync`, `ConfigureAsync`) and exposes `Service.Runner`
(the same runner). `resource.Model` is the `ModelReference` and is readable
**before** initialization (handy for logging the target model).

Streaming + embeddings example (drop into any test body once the model is loaded):

```csharp
var tokens = new List<string>();
await foreach (var token in resource.Runner.ChatStreamAsync("Count from one to five.", ct))
    tokens.Add(token);
Assert.True(tokens.Count > 1);

// Embeddings need an embedding model, not the chat default. Pull it once, then embed against it.
var embedModel = ModelReference.Parse("ai/embeddinggemma");
await resource.Runner.PullAsync(embedModel, cancellationToken: ct);
var vector = await resource.Runner.EmbedAsync("hello world", embedModel, ct);
Assert.NotEmpty(vector);
```

## Model references and pulls

The integration suite reads these env vars (override for mirrors or
smaller/larger models):

- `FLUENTDOCKER_DMR_CHAT_MODEL` (default `ai/smollm2:latest`) — a tiny chat/LLM model.
- `FLUENTDOCKER_DMR_EMBED_MODEL` (default `ai/embeddinggemma:latest`) — a tiny embeddings model.

Tags are pinned to explicit `:latest` so a test never silently targets a tag
the fixture didn't pull. First pull/load can be slow and downloads can be
hundreds of MB or more — use a generous `InitializationTimeout` for first-run
CI, or pre-pull models in setup with `runner.PullAsync(...)`.

Avoid a surprise `PullIfMissing()` in CI unless the lane explicitly permits
model downloads. The destructive FluentDocker integration test that removes
and re-pulls a host model is skipped unless
`FLUENTDOCKER_DMR_ALLOW_DESTRUCTIVE=1`.

> **DMR v1.2.1 quirk.** The bundled llama.cpp can crash
> (`GGML_ASSERT(n_outputs >= 1)` in its auto fit-to-device-memory step) when a
> chat model loads *without* an explicit context size. Pin one
> (`m.WithContextSize(4096)`) to dodge that probe — this is an engine
> workaround, not a FluentDocker requirement.

## Cleanup

Default behavior unloads the model on dispose. Opt into keeping it only when
the host intentionally owns the model lifecycle:

```csharp
new ModelResource(k, "ai/smollm2:latest", m => m.KeepRunning());      // host owns it
new ModelResource(k, "ai/smollm2:latest", m => m.KeepRunning(false)); // explicit default
```

Unload on dispose is best-effort: `ModelService` teardown is bounded by the
resource `TeardownTimeout` and logs rather than throwing on a flaky unload, so
a teardown won't fail your test. If a test must *prove* the model unloaded,
assert on `Runner.ListRunningAsync(ct)`:

```csharp
Assert.DoesNotContain(
    await resource.Runner.ListRunningAsync(ct),
    r => r.Reference.Equals(resource.Model));
```

For manual lifecycle (no fixture/helper), dispose via the null-safe core helper:

```csharp
await ResourceLifecycle.DisposeAsync(resource, kernel); // either arg may be null
```

## Model-backed containers

A container can receive the runner URL through `WithModel(...)` so the app
under test talks to DMR exactly as it would in production:

```csharp
await using var results = await new Builder().WithinDriver("docker-cli", kernel)
    .UseContainer(c => c
        .UseImage("curlimages/curl:latest")
        .WithModel(ModelReference.Parse("ai/smollm2:latest"))
        .WithCommand("sh", "-lc", "curl -fsS \"$LLM_URL/models\""))
    .BuildAsync(cancellationToken: ct);
```

Keep live DMR tests gated by the probe above.

## See also

- [core.md](core.md) — `ITestResource`, `ResourceLifecycle`, `DockerResourceOptions`.
- [xunit.md](xunit.md) · [nunit.md](nunit.md) · [mstest.md](mstest.md) — per-framework adapter details.
