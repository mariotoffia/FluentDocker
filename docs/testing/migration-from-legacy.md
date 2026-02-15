# Migration from Legacy Test Packages

This guide shows how to migrate from the legacy `FluentDocker.XUnit` and
`FluentDocker.MsTest` packages to the new `FluentDocker.Testing.Core` system
with framework adapters.

## Package Changes

| Legacy Package | New Package |
|---|---|
| `FluentDocker.XUnit` | `FluentDocker.Testing.Xunit` |
| `FluentDocker.MsTest` | `FluentDocker.Testing.MsTest` |
| (none) | `FluentDocker.Testing.NUnit` |

The legacy packages have been removed. Use the new packages listed above.

## Why Migrate?

- **Async-first lifecycle**: `InitializeAsync`/`DisposeAsync` instead of sync
  constructors.
- **Driver selection**: Choose Docker CLI, Docker API, or Podman CLI per test.
- **Diagnostics**: Automatic log capture and inspect data on failure.
- **Plugin ecosystem**: Use external plugins for Postgres, Redis, etc.
- **Lifecycle hooks**: Before/after initialize and dispose callbacks.
- **Capability checks**: Preflight validation before provisioning.

---

## xUnit: FluentDockerTestBase to XunitContainerFixture

### Before (legacy)

```csharp
using FluentDocker.XUnit;

public class RedisFixture : FluentDockerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("redis:alpine")
            .ExposePort("6379")
            .WaitForPort("6379/tcp");
    }
}

public class RedisTests : IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;
    public RedisTests(RedisFixture fixture) => _fixture = fixture;

    [Fact]
    public void IsRunning()
    {
        Assert.Equal(ServiceRunningState.Running, _fixture.Container.State);
    }
}
```

### After (new)

```csharp
using FluentDocker.Testing.Xunit;

public class RedisFixture : XunitContainerFixture
{
    public RedisFixture()
    {
        InitializeAsync(builder => builder
            .UseImage("redis:alpine")
            .ExposePort("6379")
            .WaitForPort("6379/tcp")
        ).GetAwaiter().GetResult();
    }
}

public class RedisTests : IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;
    public RedisTests(RedisFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task IsRunning()
    {
        var info = await _fixture.Container.InspectAsync();
        Assert.True(info.State.Running);
    }
}
```

**What changed:**

- Package reference: `FluentDocker.XUnit` to `FluentDocker.Testing.Xunit`
- Base class: `FluentDockerTestBase` to `XunitContainerFixture`
- Configuration: `ConfigureContainer` override to `InitializeAsync` lambda
- No kernel management needed; the fixture handles it internally

---

## MSTest: FluentDockerTestBase to MsTestResourceHelpers

### Before (legacy)

```csharp
using FluentDocker.MsTest;

[TestClass]
public class RedisTests : FluentDockerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("redis:alpine")
            .ExposePort("6379")
            .WaitForPort("6379/tcp");
    }

    [TestMethod]
    public void IsRunning()
    {
        Assert.AreEqual(ServiceRunningState.Running, Container.State);
    }
}
```

### After (new)

```csharp
using FluentDocker.Testing.MsTest;

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
                .ExposePort("6379")
                .WaitForPort("6379/tcp"));
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
    }

    [TestMethod]
    public async Task IsRunning()
    {
        var info = await _resource.Container.InspectAsync();
        Assert.IsTrue(info.State.Running);
    }
}
```

**What changed:**

- Package reference: `FluentDocker.MsTest` to `FluentDocker.Testing.MsTest`
- No more base class inheritance; uses static helper methods
- `ClassInitialize`/`ClassCleanup` manage the lifecycle
- Access container via `_resource.Container` instead of `Container`

---

## MSTest: FluentDockerComposeTestBase to MsTestResourceHelpers

### Before (legacy)

```csharp
using FluentDocker.MsTest;

[TestClass]
public class ComposeTests : FluentDockerComposeTestBase
{
    protected override void ConfigureCompose(IComposeBuilder builder)
    {
        builder
            .WithComposeFile("docker-compose.yml")
            .WithProjectName("tests");
    }
}
```

### After (new)

```csharp
using FluentDocker.Testing.MsTest;

[TestClass]
public class ComposeTests
{
    private static FluentDockerKernel _kernel;
    private static ComposeResource _resource;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        (_kernel, _resource) = await MsTestResourceHelpers.CreateComposeAsync(
            builder => builder
                .WithComposeFile("docker-compose.yml")
                .WithProjectName("tests"));
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
    }
}
```

---

## Legacy PostgresTestBase to Plugin Pattern

### Before (legacy)

```csharp
using FluentDocker.MsTest;

[TestClass]
public class MyDbTests : PostgresTestBase
{
    [TestMethod]
    public void CanConnect()
    {
        // ConnectionString is available from base class
        using var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();
    }
}
```

### After (plugin pattern)

Technology-specific fixtures like `PostgresTestBase` are no longer in core.
Use an external plugin or configure the container directly:

```csharp
using FluentDocker.Testing.MsTest;

[TestClass]
public class MyDbTests
{
    private static FluentDockerKernel _kernel;
    private static ContainerResource _resource;
    private static string _connectionString;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        (_kernel, _resource) = await MsTestResourceHelpers.CreateContainerAsync(
            builder => builder
                .UseImage("postgres:16-alpine")
                .WithEnvironment("POSTGRES_PASSWORD", "test")
                .WithPort("5432", null)
                .WaitForPort("5432/tcp"));

        // Resolve the mapped host port from inspect data
        var info = await _resource.Container.InspectAsync();
        var port = "5432";
        if (info.NetworkSettings?.Ports?.TryGetValue("5432/tcp", out var bindings) == true
            && bindings is { Length: > 0 })
            port = bindings[0].HostPort;

        _connectionString =
            $"Host=localhost;Port={port};Database=postgres;" +
            "Username=postgres;Password=test";
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
    }

    [TestMethod]
    public void CanConnect()
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
    }
}
```

Or use a plugin package like `FluentDocker.Testing.Plugin.Postgres` when
available:

```csharp
var host = new TestPluginHost();
host.Add(new PostgresPlugin(kernel));
var resource = host.Create<ContainerResource>("postgres");
await resource.InitializeAsync();
```

---

## NUnit (new support)

NUnit was not supported in legacy packages. Use `FluentDocker.Testing.NUnit`:

```csharp
using FluentDocker.Testing.NUnit;

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
        var info = await _resource.Container.InspectAsync();
        Assert.That(info.State.Running, Is.True);
    }
}
```

---

## Driver Selection

Legacy packages always used Docker CLI. New packages support driver selection:

```csharp
// Docker CLI (default)
await MsTestResourceHelpers.CreateContainerAsync(
    builder => builder.UseImage("redis:alpine"));

// Podman CLI
await MsTestResourceHelpers.CreateContainerAsync(
    builder => builder.UseImage("redis:alpine"),
    kernelFactory: async () => await FluentDockerKernel.Create()
        .WithPodmanCli("podman", d => d.AsDefault())
        .BuildAsync());

// Docker API
await MsTestResourceHelpers.CreateContainerAsync(
    builder => builder.UseImage("redis:alpine"),
    kernelFactory: async () => await FluentDockerKernel.Create()
        .WithDockerApi("docker-api", d => d.AsDefault())
        .BuildAsync());
```

---

## Quick Reference

| Concept | Legacy | New |
|---|---|---|
| xUnit base class | `FluentDockerTestBase` | `XunitContainerFixture` |
| MSTest base class | `FluentDockerTestBase` | `MsTestResourceHelpers` (static) |
| MSTest compose base | `FluentDockerComposeTestBase` | `MsTestResourceHelpers.CreateComposeAsync` |
| Postgres base class | `PostgresTestBase` | External plugin or direct config |
| NUnit support | None | `NUnitResourceHelpers` |
| Driver selection | Docker CLI only | `DockerCli`, `DockerApi`, `PodmanCli` |
| Lifecycle hooks | None | `OnBeforeInitialize`, `OnAfterReady`, etc. |
| Diagnostics | None | `CaptureLogsOnFailure`, `Diagnostics` |
