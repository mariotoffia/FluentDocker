---
layout: default
title: NUnit Adapter
parent: Testing
nav_order: 4
---

# NUnit Adapter

Package: `FluentDocker.Testing.NUnit`

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> it from source — see [Consume the preview](https://mariotoffia.github.io/FluentDocker/getting-started.html#consume-the-preview). The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## Step by Step

- Setup: [Project setup / requirements](#project-setup--requirements)
- Basics: [Fixture Base](#fixture-base), [Helper Methods](#helper-methods), [OneTimeSetUp Example](#onetimesetup-example), [Assembly-Level SetUpFixture](#assembly-level-setupfixture)
- Intermediate: [Compose Example](#compose-example), [Swarm Stack Example](#swarm-stack-example), [Podman Kubernetes Example](#podman-kubernetes-example)
- Models: [Docker Model Runner Example](#docker-model-runner-example)
- Advanced: [Generic / Custom Resource](#generic--custom-resource), [Image / Network / Volume via the Generic Path](#image--network--volume-via-the-generic-path)

## Project setup / requirements

`FluentDocker.Testing.NUnit` references `NUnit`, so referencing it gives you the
`[TestFixture]`/`[Test]` attribute set transitively. That alone is **not** runnable —
NUnit still needs the VSTest test host and adapter. A consumer test project needs:

| Package | Why | Transitive from this package? |
| --- | --- | --- |
| `Microsoft.NET.Test.Sdk` | VSTest test host | No — add explicitly |
| `NUnit3TestAdapter` | Discovers/runs `[TestFixture]`es under VSTest | No — add explicitly |
| `NUnit` | Attributes + asserts | Yes (via `FluentDocker.Testing.NUnit`); add explicitly only to pin the version |

Supported NUnit version: **4.3.2** (what `FluentDocker.Testing.NUnit` references). Minimal
consumer `.csproj` (target `net10.0`):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="NUnit" Version="4.3.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
    <PackageReference Include="FluentDocker.Testing.NUnit" Version="3.*" />
  </ItemGroup>

</Project>
```

The repo's `FluentDocker.Testing.NUnit.RunnerTests` project is a working reference: it uses
`Microsoft.NET.Test.Sdk`, `NUnit`, and `NUnit3TestAdapter` plus a project reference to
`FluentDocker.Testing.NUnit`.

> **Warning:** On a shared Docker or Podman daemon, the default
> `CleanupOrphansOnInit = true` can remove another session's eligible resources
> once they pass the one-hour `OrphanCleanupMinimumAge`: managed stopped
> containers and unused networks/volumes. Running containers and networks/volumes
> still in use are preserved. Set `FLUENTDOCKER_TEST_SESSION` for sibling
> processes, or opt out by returning `CleanupOrphansOnInit = false` from `GetOptions()`:

```csharp
protected override DockerResourceOptions GetOptions() => new()
{
    CleanupOrphansOnInit = false
};
```

See [Testing Core — Orphan Cleanup](core.md#orphan-cleanup) for the full behavior.

## Fixture Base

Recommended entry point: use `NUnitContainerFixtureBase` for container
integration suites. Use helper methods when you need custom lifetime control.

```csharp
[TestFixture]
public sealed class RedisTests : NUnitContainerFixtureBase
{
    protected override void ConfigureContainer(IContainerBuilder builder)
        => builder.UseImage("redis:alpine").WaitForPort("6379/tcp");
}
```

> **Note:** `NUnitContainerFixtureBase` gives one container **per test class**, via
> `[OneTimeSetUp]`/`[OneTimeTearDown]`. MSTest's `MsTestPerTestContainerFixtureBase`
> is per **test method** instead — check [MSTest per-test lifecycle](mstest.md#per-test-lifecycle)
> before porting fixtures across frameworks.

## Helper Methods

`NUnitResourceHelpers` provides static async methods for use with NUnit's `[OneTimeSetUp]`
and `[OneTimeTearDown]`, or per-test `[SetUp]`/`[TearDown]`.

### OneTimeSetUp Example

```csharp
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

    [Test]
    public async Task Redis_IsRunning()
    {
        var info = await _resource.InspectAsync();
        Assert.That(info.State.Running, Is.True);
    }
}
```

### Assembly-Level SetUpFixture

```csharp
[SetUpFixture]
public class GlobalDockerSetup
{
    private FluentDockerKernel _kernel;
    private ContainerResource _resource;

    [OneTimeSetUp]
    public async Task Setup()
    {
        (_kernel, _resource) = await NUnitResourceHelpers.CreateContainerAsync(
            builder => builder.UseImage("redis:alpine"));
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await NUnitResourceHelpers.DisposeAsync(_resource, _kernel);
    }
}
```

### Compose Example

```csharp
[OneTimeSetUp]
public async Task Setup()
{
    (_kernel, _resource) = await NUnitResourceHelpers.CreateComposeAsync(
        c => c
            .WithComposeFile("docker-compose.yml")
            .WithRemoveOrphans());
}
```

### Swarm Stack Example

```csharp
[OneTimeSetUp]
public async Task Setup()
{
    (_kernel, _resource) = await NUnitResourceHelpers.CreateSwarmStackAsync(
        new StackDeployConfig
        {
            StackName = $"my-stack-{Guid.NewGuid():N}", // unique — parallel-safe
            ComposeFiles = { "docker-compose.yml" }
        });
}
```

### Podman Kubernetes Example

```csharp
[OneTimeSetUp]
public async Task Setup()
{
    (_kernel, _resource) = await NUnitResourceHelpers.CreatePodmanKubernetesAsync(
        new KubePlayConfig { YamlPath = "pod.yaml" },
        kernelFactory: async () => await FluentDockerKernel.Create()
            .WithPodmanCli("podman", d => d.AsDefault())
            .BuildAsync());
}
```

### Docker Model Runner Example

A `ModelResource` loads a Docker Model Runner (DMR) model for the lifetime of the
fixture and exposes an `IModelRunner` for chat / embeddings. Built via the generic
`CreateResourceAsync<ModelResource>` helper.

Probe DMR first so the test skips cleanly when it is not running. On CI must-run
lanes set `FLUENTDOCKER_REQUIRE_DMR=1` to hard-fail instead of skipping. Store
BOTH the kernel and the resource in fields so `NUnitResourceHelpers.DisposeAsync`
can tear both down — it is null-safe, so a fixture that ignored before assignment
leaves both fields null and teardown is a no-op (no `!` needed).

The example is complete and compiles with nullable enabled:

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

        // Probe DMR on a throwaway kernel so we can skip BEFORE allocating the resource.
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
            // PR/local: skip. CI must-run lanes set FLUENTDOCKER_REQUIRE_DMR=1 → hard-fail.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLUENTDOCKER_REQUIRE_DMR")))
                Assert.Ignore("Docker Model Runner is not running.");
            throw new InvalidOperationException(
                "FLUENTDOCKER_REQUIRE_DMR=1 but Docker Model Runner is not running.");
        }

        // Load the model. ModelResource pulls (if missing) and starts it; the helper
        // owns the kernel returned in the tuple, so capture it for teardown.
        (_kernel, _model) = await NUnitResourceHelpers.CreateResourceAsync<ModelResource>(
            k => new ModelResource(k, "ai/smollm2:latest", m => m.WithContextSize(4096)),
            cancellationToken: ct);
    }

    [OneTimeTearDown]
    public Task Teardown()
        // Null-safe: both fields may be null if Setup ignored before assignment.
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

Resource members:

- `_model.Runner` — the `IModelRunner` (chat, streaming chat, embeddings).
- `_model.Service` — the underlying `IModelService` handle.
- `_model.Model` — the parsed `ModelReference` (readable even before init, for logging).

> Note: cleanup uses `NUnitResourceHelpers.DisposeAsync(resource, kernel)` — the
> adapter helper — not `ResourceLifecycle.DisposeAsync` directly. They forward to
> the same logic, but use the adapter helper in NUnit fixtures for consistency.

### Generic / Custom Resource

Use `CreateResourceAsync<T>` for plugin or custom `ITestResource` types:

```csharp
[OneTimeSetUp]
public async Task Setup()
{
    (_kernel, _resource) = await NUnitResourceHelpers.CreateResourceAsync<ContainerResource>(
        kernel => new ContainerResource(kernel,
            c => c.UseImage("redis:alpine").WaitForPort("6379/tcp")));
}
```

### Image / Network / Volume via the Generic Path

`NUnitResourceHelpers` ships typed helpers for containers, Compose, topologies,
swarm stacks, and Podman Kubernetes. Image, network, and volume resources don't
have dedicated helpers — create them through the same generic
`CreateResourceAsync<T>(k => new XxxResource(k, ...))` path. The kernel returned
in the tuple is owned by the helper; dispose both in teardown.

`ImageResource` pulls an image (and optionally removes it on dispose):

```csharp
// ctor: (kernel, image, tag = "latest", removeOnDispose = false, options = null)
(_kernel, _image) = await NUnitResourceHelpers.CreateResourceAsync<ImageResource>(
    k => new ImageResource(k, "nginx", "alpine", removeOnDispose: true));
// _image.ImageReference -> "nginx:alpine"; _image.ImageId after init.
```

`NetworkResource` takes a `NetworkCreateConfig` callback
(`FluentDocker.Drivers`); a unique name is generated when `Name` is left empty:

```csharp
using FluentDocker.Drivers; // NetworkCreateConfig

(_kernel, _network) = await NUnitResourceHelpers.CreateResourceAsync<NetworkResource>(
    k => new NetworkResource(k, cfg =>
    {
        cfg.Name = $"test-net-{Guid.NewGuid():N}"; // omit to auto-generate
        cfg.Driver = "bridge";
    }));
// _network.NetworkName / _network.NetworkId after init.
```

`VolumeResource` takes a `VolumeCreateConfig` callback (`FluentDocker.Drivers`):

```csharp
using FluentDocker.Drivers; // VolumeCreateConfig

(_kernel, _volume) = await NUnitResourceHelpers.CreateResourceAsync<VolumeResource>(
    k => new VolumeResource(k, cfg =>
    {
        cfg.Name = $"test-vol-{Guid.NewGuid():N}"; // omit to auto-generate
        cfg.Driver = "local";
    }));
// _volume.VolumeName after init.
```

Tear any of these down with the same helper:

```csharp
[OneTimeTearDown]
public Task Teardown() => NUnitResourceHelpers.DisposeAsync(_image, _kernel);
```
