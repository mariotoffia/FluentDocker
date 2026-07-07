---
layout: default
title: MSTest Adapter
parent: Testing
nav_order: 3
---

# MSTest Adapter

Package: `FluentDocker.Testing.MsTest`

## Step by Step

- Setup: [Project setup / requirements](#project-setup--requirements)
- Basics: [Helper Methods](#helper-methods), [Container Example](#container-example), [Per-Test Lifecycle](#per-test-lifecycle)
- Intermediate: [Compose Example](#compose-example), [Swarm Stack Example](#swarm-stack-example), [Podman Kubernetes Example](#podman-kubernetes-example)
- Advanced: [Generic / Custom Resource](#generic--custom-resource), [Image / Network / Volume (generic path)](#image--network--volume-generic-path), [Docker Model Runner (ModelResource)](#docker-model-runner-modelresource)

## Project setup / requirements

`FluentDocker.Testing.MsTest` references `MSTest.TestFramework`, so referencing
it gives you the `[TestClass]` / `[TestMethod]` attribute set transitively.
That alone is **not** runnable — MSTest still needs the test host and adapter.
A consumer test project needs:

| Package | Why | Transitive from this package? |
| --- | --- | --- |
| `Microsoft.NET.Test.Sdk` | VSTest test host | No — add explicitly |
| `MSTest.TestAdapter` | Discovers/runs `[TestClass]`es | No — add explicitly |
| `MSTest.TestFramework` | Attributes + asserts | Yes (via `FluentDocker.Testing.MsTest`); add explicitly only to pin the version |

Supported MSTest version: **3.7.3** (what `FluentDocker.Testing.MsTest`
references). Minimal consumer `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.7.3" />
    <PackageReference Include="MSTest.TestFramework" Version="3.7.3" />
    <PackageReference Include="FluentDocker.Testing.MsTest" Version="3.*" />
  </ItemGroup>

</Project>
```

`<IsPackable>false</IsPackable>` is unnecessary for a consumer test project
(omit it). The repo's own `FluentDocker.Testing.MsTest.RunnerTests` project is a
working reference: it uses exactly `Microsoft.NET.Test.Sdk`,
`MSTest.TestAdapter`, and `MSTest.TestFramework` plus a `ProjectReference` to
`FluentDocker.Testing.MsTest`. As an alternative, the modern `MSTest.Sdk`
meta-package (`<Project Sdk="MSTest.Sdk/3.7.3">`) bundles host + adapter +
framework; then you only add `FluentDocker.Testing.MsTest`.

> **Warning:** On a shared Docker or Podman daemon, the default
> `CleanupOrphansOnInit = true` lets a test run force-remove **another** session's
> FluentDocker-managed containers, networks, and volumes once they pass the one-hour
> `OrphanCleanupMinimumAge`. On shared CI agents that can delete a parallel job's live
> resources. Opt out by returning `CleanupOrphansOnInit = false` from `GetOptions()` (or
> by passing a `DockerResourceOptions` to the helper methods).

```csharp
protected override DockerResourceOptions GetOptions() => new()
{
    CleanupOrphansOnInit = false
};
```

See [Testing Core — Orphan Cleanup](core.md#orphan-cleanup) for the full behavior.

## Helper Methods

`MsTestResourceHelpers` provides static async methods for creating and disposing resources.

### Container Example

```csharp
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

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)] // pin to class scope — MSTest 3.x defaults to end-of-assembly
    public static async Task ClassCleanup()
    {
        await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
    }

    [TestMethod]
    public async Task Redis_IsRunning()
    {
        var info = await _resource.InspectAsync();
        Assert.IsTrue(info.State.Running);
    }
}
```

### Per-Test Lifecycle

`MsTestContainerFixtureBase` is intentionally per-test-method. Use it when each
test needs a fresh container. For a class-shared container, use the generic
`MsTestClassContainerFixtureBase<TFixture>` pattern below.

> **Note:** Watch the base-class names when porting from xUnit or NUnit. There,
> `*ContainerFixtureBase` gives one container **per test class**. In MSTest,
> `MsTestContainerFixtureBase` gives one **per test method**; the per-class equivalent is
> `MsTestClassContainerFixtureBase<TFixture>`.

```csharp
[TestClass]
public class PerTestRedisTests
{
    private FluentDockerKernel _kernel;
    private ContainerResource _resource;

    [TestInitialize]
    public async Task Setup()
    {
        (_kernel, _resource) = await MsTestResourceHelpers.CreateContainerAsync(
            builder => builder.UseImage("redis:alpine"));
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
    }
}
```

### Per-Class Fixture Base

`[ClassCleanup(ClassCleanupBehavior.EndOfClass)]` is mandatory for this pattern.
MSTest 3.x runs a bare `[ClassCleanup]` at end-of-assembly, leaking the container
until the whole run ends. Fixed container names can collide when test classes run
in parallel. Pass the most-derived class as the generic argument; reusing a base
class generic shares static container state.

```csharp
[TestClass]
public class SharedRedisTests : MsTestClassContainerFixtureBase<SharedRedisTests>
{
    protected override void ConfigureContainer(IContainerBuilder builder)
        => builder
            .UseImage("redis:alpine")
            .WithName($"redis-tests-{Guid.NewGuid():N}")
            .WaitForPort("6379/tcp");

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static Task ClassCleanup()
        => CleanupClassAsync();

    [TestMethod]
    public async Task Redis_IsRunning()
    {
        var info = await Resource.InspectAsync();
        Assert.IsTrue(info.State.Running);
    }
}
```

### Compose Example

```csharp
[ClassInitialize]
public static async Task ClassInit(TestContext context)
{
    (_kernel, _resource) = await MsTestResourceHelpers.CreateComposeAsync(
        builder => builder
            .WithComposeFile("docker-compose.yml")
            .WithProjectName($"integration-tests-{Guid.NewGuid():N}")); // unique — parallel-safe
}
```

### Swarm Stack Example

```csharp
[ClassInitialize]
public static async Task ClassInit(TestContext context)
{
    (_kernel, _resource) = await MsTestResourceHelpers.CreateSwarmStackAsync(
        new StackDeployConfig
        {
            StackName = $"my-stack-{Guid.NewGuid():N}", // unique — parallel-safe
            ComposeFiles = { "docker-compose.yml" }
        });
}
```

### Podman Kubernetes Example

```csharp
[ClassInitialize]
public static async Task ClassInit(TestContext context)
{
    (_kernel, _resource) = await MsTestResourceHelpers.CreatePodmanKubernetesAsync(
        new KubePlayConfig { YamlPath = "pod.yaml" },
        kernelFactory: async () => await FluentDockerKernel.Create()
            .WithPodmanCli("podman", d => d.AsDefault())
            .BuildAsync());
}
```

### Generic / Custom Resource

Use `CreateResourceAsync<T>` for plugin or custom `ITestResource` types:

```csharp
[ClassInitialize]
public static async Task ClassInit(TestContext context)
{
    (_kernel, _resource) = await MsTestResourceHelpers.CreateResourceAsync<ContainerResource>(
        kernel => new ContainerResource(kernel,
            c => c.UseImage("redis:alpine").WaitForPort("6379/tcp")));
}
```

### Image / Network / Volume (generic path)

There are no typed `CreateImageAsync` / `CreateNetworkAsync` /
`CreateVolumeAsync` helpers — create those resources through the same generic
`CreateResourceAsync<T>` path. The constructors live in
`FluentDocker.Testing.Core`:

```csharp
using FluentDocker.Drivers;          // NetworkCreateConfig, VolumeCreateConfig
using FluentDocker.Testing.Core;     // ImageResource, NetworkResource, VolumeResource

// Image: pull on init; pass removeOnDispose: true to also remove it on cleanup.
(_kernel, var image) = await MsTestResourceHelpers.CreateResourceAsync<ImageResource>(
    k => new ImageResource(k, "alpine", tag: "3.20", removeOnDispose: false));

// Network: configure via the NetworkCreateConfig callback (name auto-generated if unset).
(_kernel, var network) = await MsTestResourceHelpers.CreateResourceAsync<NetworkResource>(
    k => new NetworkResource(k, cfg => cfg.Driver = "bridge"));

// Volume: configure via the VolumeCreateConfig callback (name auto-generated if unset).
(_kernel, var volume) = await MsTestResourceHelpers.CreateResourceAsync<VolumeResource>(
    k => new VolumeResource(k, cfg => cfg.Driver = "local"));
```

Dispose each with `await MsTestResourceHelpers.DisposeAsync(resource, _kernel);`
in `[ClassCleanup]` / `[TestCleanup]`, exactly like the typed helpers.

### Docker Model Runner (ModelResource)

`CreateResourceAsync<ModelResource>` loads a Docker Model Runner model for the
test and unloads it on cleanup. Probe the runner first and skip cleanly when it
is unavailable, unless `FLUENTDOCKER_REQUIRE_DMR=1` (then hard-fail). Dispose
with the adapter helper — `MsTestResourceHelpers.DisposeAsync(resource, kernel)`
— not `ResourceLifecycle.DisposeAsync`.

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
    public static async Task ClassInit(TestContext context)
    {
        var ct = context.CancellationTokenSource.Token;

        // Probe Docker Model Runner with its own short-lived kernel.
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

`StatusAsync` takes only `(DriverContext, ct)` — no model argument — and the
flag is `status.Data.Running`. `_model.Runner` is the `IModelRunner`,
`_model.Service` the `IModelService`, and `_model.Model` the parsed
`ModelReference`. For per-test isolation, move the probe + create into
`[TestInitialize]` and the dispose into `[TestCleanup]` with instance fields.
