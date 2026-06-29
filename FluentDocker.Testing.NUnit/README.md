# FluentDocker.Testing.NUnit

NUnit test helpers for FluentDocker.

## Install

```bash
dotnet add package FluentDocker.Testing.NUnit
```

## Base-class fixture

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

Full docs: `docs/testing/nunit.md`. Model testing guide: `docs/testing/model.md`.
