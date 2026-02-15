# Plugin System

Package: `FluentDocker` (built-in under `FluentDocker.Testing.Core.Plugins`)

## Overview

The plugin system allows external assemblies to provide focused test resources
(e.g., Postgres, RabbitMQ, Redis) while FluentDocker core remains generic.

Plugins register resource factories with a host, and tests resolve them by type
or key.

## Core Contracts

### `ITestPlugin`

Entry point for a plugin assembly. Each plugin has a unique `Id` and registers
its factories during startup.

```csharp
public interface ITestPlugin
{
    string Id { get; }
    void Register(ITestPluginRegistry registry);
}
```

### `ITestPluginRegistry`

Passed to `ITestPlugin.Register()`. Plugins call `RegisterFactory` to make their
resources available.

```csharp
public interface ITestPluginRegistry
{
    void RegisterFactory<TResource>(
        string key,
        Func<IServiceProvider, TResource> factory)
        where TResource : class, ITestResource;
}
```

### `ITestPluginHost`

Manages plugins and resolves resource factories at test time.

```csharp
public interface ITestPluginHost
{
    ITestPluginHost Add(ITestPlugin plugin);
    TResource Create<TResource>() where TResource : class, ITestResource;
    TResource Create<TResource>(string key) where TResource : class, ITestResource;
    bool HasFactory(string key);
}
```

## Writing a Plugin

### 1. Create the plugin class

Plugins receive an `IServiceProvider` in their factory. If you need the kernel,
accept it as a constructor parameter so the plugin works with or without DI:

```csharp
// In a separate package, e.g., FluentDocker.Testing.Plugin.Postgres
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Core.Plugins;

public class PostgresPlugin : ITestPlugin
{
    private readonly FluentDockerKernel _kernel;

    public PostgresPlugin(FluentDockerKernel kernel)
        => _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));

    public string Id => "FluentDocker.Testing.Plugin.Postgres";

    public void Register(ITestPluginRegistry registry)
    {
        registry.RegisterFactory<ContainerResource>(
            "postgres",
            _ => new ContainerResource(
                _kernel,
                builder => builder
                    .UseImage("postgres:16-alpine")
                    .WithEnvironment("POSTGRES_PASSWORD", "test")
                    .ExposePort("5432")
                    .WaitForPort("5432/tcp")));
    }
}
```

### 2. Reference only `FluentDocker`

Plugin packages should depend on:

- `FluentDocker` (contains `FluentDocker.Testing.Core` and plugin contracts)

They should **not** depend on framework adapters unless framework-specific sugar
is intentionally added.

### 3. Package naming convention

| Package | Purpose |
|---|---|
| `FluentDocker.Testing.Plugin.Postgres` | Postgres resource factory |
| `FluentDocker.Testing.Plugin.RabbitMq` | RabbitMQ resource factory |
| `FluentDocker.Testing.Plugin.Redis` | Redis resource factory |
| `FluentDocker.Testing.Plugin.Kafka` | Kafka resource factory |
| `FluentDocker.Testing.Plugin.PodmanKubeRecipes` | Podman kube recipes |

Optional framework companion: `FluentDocker.Testing.Plugin.Postgres.Xunit`

## Using Plugins in Tests

### Basic usage

```csharp
using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

var host = new TestPluginHost();
host.Add(new PostgresPlugin(kernel));

var resource = host.Create<ContainerResource>("postgres");
await resource.InitializeAsync();

// Use the container...

await resource.DisposeAsync();
```

### With xUnit (generic fixture)

Use `XunitResourceFixture<TResource>` to wrap any plugin resource:

```csharp
public class PostgresFixture : XunitResourceFixture<ContainerResource>
{
    public PostgresFixture()
    {
        InitializeAsync(kernel =>
        {
            var host = new TestPluginHost();
            host.Add(new PostgresPlugin(kernel));
            return host.Create<ContainerResource>("postgres");
        }).GetAwaiter().GetResult();
    }
}

public class PostgresTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;
    public PostgresTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public void Resource_IsInitialized() => Assert.True(_fixture.Resource.IsInitialized);
}
```

### With MSTest (generic helper)

Use `MsTestResourceHelpers.CreateResourceAsync<T>`:

```csharp
[TestClass]
public class PostgresTests
{
    private static FluentDockerKernel _kernel;
    private static ContainerResource _resource;

    [ClassInitialize]
    public static async Task Init(TestContext ctx)
    {
        (_kernel, _resource) = await MsTestResourceHelpers.CreateResourceAsync<ContainerResource>(
            kernel =>
            {
                var host = new TestPluginHost();
                host.Add(new PostgresPlugin(kernel));
                return host.Create<ContainerResource>("postgres");
            });
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
    }
}
```

### With NUnit (generic helper)

```csharp
[TestFixture]
public class PostgresTests
{
    private FluentDockerKernel _kernel;
    private ContainerResource _resource;

    [OneTimeSetUp]
    public async Task Setup()
    {
        (_kernel, _resource) = await NUnitResourceHelpers.CreateResourceAsync<ContainerResource>(
            kernel =>
            {
                var host = new TestPluginHost();
                host.Add(new PostgresPlugin(kernel));
                return host.Create<ContainerResource>("postgres");
            });
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await NUnitResourceHelpers.DisposeAsync(_resource, _kernel);
    }
}
```

## Key Behaviors

- **Plugin Id required**: `ITestPlugin.Id` must not be null or empty; `Add()`
  throws `ArgumentException` otherwise.
- **Idempotent registration**: Adding the same plugin twice (same `Id`) is safe;
  the second call is ignored.
- **Duplicate key rejection**: If two different plugins register the same factory
  key, `Add()` throws `InvalidOperationException`. Use unique keys per plugin.
- **Key-based lookup**: `Create<T>("key")` resolves the factory registered under
  that key.
- **Type-based lookup**: `Create<T>()` (no key) uses `typeof(T).Name` as the key.
- **Null service provider**: If no `IServiceProvider` is passed to
  `TestPluginHost`, a minimal provider returning `null` for all types is used.
  Factories that call `sp.GetRequiredService<T>()` will fail in this mode.
- **Error on missing factory**: `Create<T>()` throws `InvalidOperationException`
  if no matching factory is registered.

## Plugin Project Template

Minimal `.csproj` for a plugin package:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>FluentDocker.Testing.Plugin.MyService</PackageId>
    <Description>MyService test resource for FluentDocker</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentDocker" Version="3.*" />
  </ItemGroup>
</Project>
```
