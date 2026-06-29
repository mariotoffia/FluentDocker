---
layout: default
title: Model Runner Testing
parent: Testing
nav_order: 6
---

# Testing Docker Model Runner

`ModelResource` gives a single Docker Model Runner model the same fixture
lifecycle as a container: initialize loads the model, dispose unloads it unless
you opt into `KeepRunning()`.

## Probe DMR and Skip Cleanly

Probe the runtime port before running model tests:

```csharp
var runtime = kernel.SysCtl<IModelRuntimeDriver>("docker");
var status = await runtime.StatusAsync(
    new DriverContext("docker"),
    cancellationToken);

if (!status.Success || !status.Data.Running)
    // skip this test/suite
```

xUnit v3 dynamic skip:

```csharp
throw new InvalidOperationException("$XunitDynamicSkip$Docker Model Runner is not running");
```

NUnit:

```csharp
Assert.Ignore("Docker Model Runner is not running");
```

MSTest:

```csharp
Assert.Inconclusive("Docker Model Runner is not running");
```

Set `FLUENTDOCKER_REQUIRE_DMR=1` only on lanes where missing DMR should fail
instead of skip.

## xUnit Fixture

```csharp
using FluentDocker.Builders;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;

public sealed class ChatModelFixture : XunitResourceFixture<ModelResource>
{
    public ChatModelFixture()
    {
        Configure(k => new ModelResource(
            k,
            Environment.GetEnvironmentVariable("FLUENTDOCKER_DMR_CHAT_MODEL")
                ?? "ai/smollm2:latest",
            m => m.WithContextSize(4096),
            new DockerResourceOptions
            {
                Driver = DriverSelection.Specific("docker"),
                InitializationTimeout = TimeSpan.FromMinutes(10)
            }));
    }
}
```

Use `fixture.Resource.Runner` for inference and `fixture.Resource.Service` for
the lifecycle handle.

## MSTest Helper

```csharp
var (_, resource) = await MsTestResourceHelpers.CreateResourceAsync(
    k => new ModelResource(
        k,
        "ai/smollm2:latest",
        m => m.WithContextSize(4096),
        new DockerResourceOptions
        {
            Driver = DriverSelection.Specific("docker"),
            InitializationTimeout = TimeSpan.FromMinutes(10)
        }),
    cancellationToken: CancellationToken.None);
```

## NUnit Helper

```csharp
var (_, resource) = await NUnitResourceHelpers.CreateResourceAsync(
    k => new ModelResource(
        k,
        "ai/smollm2:latest",
        m => m.WithContextSize(4096),
        new DockerResourceOptions
        {
            Driver = DriverSelection.Specific("docker"),
            InitializationTimeout = TimeSpan.FromMinutes(10)
        }),
    cancellationToken);
```

## Model References and Pulls

The test suite reads:

- `FLUENTDOCKER_DMR_CHAT_MODEL` (default `ai/smollm2:latest`)
- `FLUENTDOCKER_DMR_EMBED_MODEL` (default `ai/embeddinggemma:latest`)

First pull/load can be slow and model downloads can be hundreds of MB or more.
Use longer initialization timeouts for first-run CI or pre-pull models in setup.

Avoid surprise `PullIfMissing()` in CI unless the lane explicitly permits model
downloads. The destructive FluentDocker integration test that removes and
re-pulls a host model is skipped unless `FLUENTDOCKER_DMR_ALLOW_DESTRUCTIVE=1`.

## Cleanup

Prefer the default unload-on-dispose behavior:

```csharp
new ModelResource(k, "ai/smollm2:latest", m => m.KeepRunning(false));
```

Use `KeepRunning()` only when the host intentionally owns the model lifecycle.

Unload on dispose is best-effort: `ModelService.DisposeAsync` logs but never throws if
the runner can't unload, so a `ModelResource` teardown won't fail your test on a flaky
unload. Assert on `Runner.ListRunningAsync` if a test must prove the model was unloaded.

## Container Reachability Sample

Model-backed containers can receive the runner URL through `WithModel(...)`:

```csharp
await new Builder().WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("curlimages/curl:latest")
        .WithModel(ModelReference.Parse("ai/smollm2:latest"))
        .WithCommand("sh", "-lc", "curl -fsS \"$LLM_URL/models\""))
    .BuildAsync(cancellationToken: cancellationToken);
```

This is a documentation sample only; keep live DMR tests gated by the probe
above.
