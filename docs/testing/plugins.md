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
        where TResource : class, IDockerResource;
}
```

### `ITestPluginHost`

Manages plugins and resolves resource factories at test time.

```csharp
public interface ITestPluginHost
{
    ITestPluginHost Add(ITestPlugin plugin);
    TResource Create<TResource>() where TResource : class, IDockerResource;
    TResource Create<TResource>(string key) where TResource : class, IDockerResource;
    bool HasFactory(string key);
}
```

## Writing a Plugin

### 1. Create the plugin class

```csharp
// In a separate package, e.g., FluentDocker.Testing.Plugin.Postgres
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Core.Plugins;

public class PostgresPlugin : ITestPlugin
{
    public string Id => "FluentDocker.Testing.Plugin.Postgres";

    public void Register(ITestPluginRegistry registry)
    {
        registry.RegisterFactory<ContainerResource>(
            "postgres",
            sp => new ContainerResource(
                sp.GetRequiredService<FluentDockerKernel>(),
                builder => builder
                    .UseImage("postgres:16-alpine")
                    .WithEnvironment("POSTGRES_PASSWORD", "test")
                    .WithPort("5432", null)
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
var host = new TestPluginHost();
host.Add(new PostgresPlugin());

var resource = host.Create<ContainerResource>("postgres");
await resource.InitializeAsync();

// Use the container...

await resource.DisposeAsync();
```

### With dependency injection

```csharp
var services = new ServiceCollection();
services.AddSingleton(kernel);
var sp = services.BuildServiceProvider();

var host = new TestPluginHost(sp);
host.Add(new PostgresPlugin());

var resource = host.Create<ContainerResource>("postgres");
```

### With xUnit v3

```csharp
public class PostgresFixture : XunitContainerFixture
{
    private readonly TestPluginHost _host = new();

    public PostgresFixture()
    {
        _host.Add(new PostgresPlugin());
        // Or use the plugin's factory directly in InitializeAsync
    }
}
```

### With MSTest

```csharp
[TestClass]
public class PostgresTests
{
    private static TestPluginHost _host;
    private static ContainerResource _resource;

    [ClassInitialize]
    public static async Task Init(TestContext ctx)
    {
        _host = new TestPluginHost();
        _host.Add(new PostgresPlugin());
        _resource = _host.Create<ContainerResource>("postgres");
        await _resource.InitializeAsync();
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        await _resource.DisposeAsync();
    }
}
```

## Key Behaviors

- **Idempotent registration**: Adding the same plugin twice (same `Id`) is safe;
  the second call is ignored.
- **Key-based lookup**: `Create<T>("key")` resolves the factory registered under
  that key.
- **Type-based lookup**: `Create<T>()` (no key) uses `typeof(T).Name` as the key.
- **Null service provider**: If no `IServiceProvider` is passed to
  `TestPluginHost`, a minimal provider returning `null` for all types is used.
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
