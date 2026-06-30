# FluentDocker.Testing.MsTest

MSTest test helpers for FluentDocker.

## Install

```bash
dotnet add package FluentDocker.Testing.MsTest
```

## Base-class fixture

```csharp
using FluentDocker.Builders;
using FluentDocker.Testing.MsTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class RedisTests : MsTestContainerFixtureBase
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

[ClassCleanup]
public static async Task ClassCleanup()
    => await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
```

Full docs: `docs/testing/mstest.md`. Model testing guide: `docs/testing/model.md`.

## Model resource

`CreateResourceAsync` builds any `ITestResource`, including a `ModelResource`.
Capture the kernel and dispose both in cleanup:

```csharp
private static FluentDockerKernel? _kernel;
private static ModelResource? _model;

[ClassInitialize]
public static async Task ClassInitialize(TestContext context)
{
  (_kernel, _model) = await MsTestResourceHelpers.CreateResourceAsync(
      k => new ModelResource(k, "ai/smollm2:latest",
          m => m.WithContextSize(4096)));
}

[ClassCleanup]
public static async Task ClassCleanup()
    => await ResourceLifecycle.DisposeAsync(_model, _kernel);
```
