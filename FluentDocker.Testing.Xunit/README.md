# FluentDocker.Testing.Xunit

xUnit v3 test helpers for FluentDocker. This package requires xUnit v3 (`xunit.v3`) and is not compatible with xUnit 2.x.

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

## Helper API

```csharp
var (kernel, resource) = await XunitResourceHelpers.CreateContainerAsync(
    c => c.UseImage("postgres:16-alpine"));

try
{
  // use resource.Container
}
finally
{
  await XunitResourceHelpers.DisposeAsync(resource, kernel);
}
```

Full docs: `docs/testing/xunit.md`. Model testing guide: `docs/testing/model.md`.
