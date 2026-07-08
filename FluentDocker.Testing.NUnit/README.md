# FluentDocker.Testing.NUnit

NUnit test helpers for FluentDocker. Spin up and tear down Docker / Podman
containers, networks, volumes, Compose stacks, and Docker Model Runner models
(via `ModelResource`) directly in your NUnit tests, with the full resource
lifecycle managed for you.

## Install

```bash
dotnet add package FluentDocker.Testing.NUnit
```

## Base-class fixture

Recommended entry point: use `NUnitContainerFixtureBase` for container
integration suites. Use `NUnitResourceHelpers` only when you need custom
lifetime control.

## Fixture lifetime (important)

| Helper | Container lifetime | Use when |
| --- | --- | --- |
| `NUnitContainerFixtureBase` | **Per test class** (`[OneTimeSetUp]`/`[OneTimeTearDown]`) | The class intentionally shares one fixture. |
| `NUnitResourceHelpers.CreateContainerAsync` | Caller-controlled | You need method-level or custom lifetime control. |

## Best-effort crash cleanup

Normal cleanup runs during fixture disposal and the next initialization orphan sweep. Set
`FLUENTDOCKER_TEST_SESSION=<shared-id>` to group parallel test processes into one live session.
Set `FLUENTDOCKER_TEST_REAPER_ON_EXIT=1` to opt in to process-exit/SIGINT/SIGTERM cleanup for
the current session. Shared `FLUENTDOCKER_TEST_SESSION` runs skip exit reaping to avoid deleting
sibling processes; SIGKILL and hard CI termination cannot run in-process cleanup.

```csharp
using FluentDocker.Builders;
using FluentDocker.Testing.NUnit;
using NUnit.Framework;

[TestFixture]
public sealed class RedisTests : NUnitContainerFixtureBase
{
  protected override void ConfigureContainer(IContainerBuilder builder)
      => builder.UseImage("redis:7-alpine");

  [Test]
  public async Task Redis_IsRunning()
  {
    var info = await Container.InspectAsync();
    Assert.That(info.State.Status, Is.EqualTo("running"));
  }
}
```

`Resource`, `Container`, and `Kernel` are non-null after `OneTimeSetUp`. Accessing them earlier throws `InvalidOperationException`.

## Helper API

```csharp
var (kernel, resource) = await NUnitResourceHelpers.CreateContainerAsync(
    c => c.UseImage("postgres:16-alpine"));

try
{
  // use resource.Container
}
finally
{
  await NUnitResourceHelpers.DisposeAsync(resource, kernel);
}
```

## Model resource (Docker Model Runner)

`CreateResourceAsync` builds any `ITestResource`, including a `ModelResource`
that loads a Docker Model Runner (DMR) model for the duration of the fixture.
Probe DMR first and skip cleanly when it is not running, then capture BOTH the
kernel and the resource so `NUnitResourceHelpers.DisposeAsync` can tear both
down. The example below is complete and compiles with nullable enabled.

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
using FluentDocker.Testing.NUnit;
using NUnit.Framework;

[TestFixture]
public sealed class SmolLmModelTests
{
  private FluentDockerKernel? _kernel;
  private ModelResource? _model;

  [OneTimeSetUp]
  public async Task Setup()
  {
    var ct = CancellationToken.None;

    // Probe DMR via its own kernel so we can skip before allocating the resource.
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
      // PR/local: skip cleanly. CI must-run lanes set FLUENTDOCKER_REQUIRE_DMR=1 → hard-fail.
      if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLUENTDOCKER_REQUIRE_DMR")))
        Assert.Ignore("Docker Model Runner is not running.");
      throw new InvalidOperationException("FLUENTDOCKER_REQUIRE_DMR=1 but Docker Model Runner is not running.");
    }

    // Load the model. ModelResource pulls (if missing) and starts it.
    (_kernel, _model) = await NUnitResourceHelpers.CreateResourceAsync<ModelResource>(
        k => new ModelResource(k, "ai/smollm2:latest", m => m.WithContextSize(4096)),
        cancellationToken: ct);
  }

  [OneTimeTearDown]
  public Task Teardown()
      // Null-safe: if Setup ignored before assignment, both fields stay null and this is a no-op.
      => NUnitResourceHelpers.DisposeAsync(_model, _kernel);

  [Test]
  public async Task Chat_Replies()
  {
    var reply = await _model!.Runner.ChatAsync("Reply with a single word.");
    Assert.That(reply, Is.Not.Empty);
  }

  [Test]
  public async Task Embed_ReturnsVector()
  {
    // Embeddings need an embedding model, not the chat default. Pull it once, then embed against it.
    var embedModel = ModelReference.Parse("ai/embeddinggemma");
    await _model!.Runner.PullAsync(embedModel);
    var vector = await _model!.Runner.EmbedAsync("hello world", embedModel);
    Assert.That(vector, Is.Not.Empty);
  }
}
```

`_model.Service` is the underlying `IModelService`; `_model.Model` is the parsed
`ModelReference`; `_model.Runner` is the `IModelRunner` for chat/embeddings.

Full docs: [docs/testing/nunit.md](https://mariotoffia.github.io/FluentDocker/testing/nunit.html). Model testing guide:
[docs/testing/model.md](https://mariotoffia.github.io/FluentDocker/testing/model.html).
