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
call `Configure(...)` the same way:

```csharp
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

Full docs: `docs/testing/xunit.md`. Model testing guide: `docs/testing/model.md`.
