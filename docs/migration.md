---
layout: default
title: Migration Guide
nav_order: 11
has_children: true
---

# Migrating to FluentDocker v3

This guide helps you migrate from v2.x.x to the FluentDocker v3 line.

> **Preview docs — not on NuGet yet.** These track the upcoming **3.2.0-preview.2** API; build from
> [`master`](https://github.com/mariotoffia/FluentDocker) to use it. The latest published package is
> **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these
> samples against 3.1.0.

## Step by Step

- Basics: [Breaking Changes Summary](#breaking-changes-summary), [Step 1: Update NuGet Packages](#step-1-update-nuget-packages), [Step 2: Update Namespaces](#step-2-update-namespaces), [Step 3: Create a Kernel](#step-3-create-a-kernel)
- Intermediate: [Step 4: Update Builder API](#step-4-update-builder-api), [Step 5: Update Test Base Classes](#step-5-update-test-base-classes), [Step 6: Remove Docker Machine Code](#step-6-remove-docker-machine-code), [Step 7: Update Compose Commands](#step-7-update-compose-commands)
- Advanced: [Step 8: Switch to Microsoft.Extensions.Logging](#step-8-switch-to-microsoftextensionslogging), [Removed Features](#removed-features), [Detailed Migration Resources](#detailed-migration-resources)

## Breaking Changes Summary

| Change | Impact | Action |
|--------|--------|--------|
| Namespace: `Ductus.FluentDocker` → `FluentDocker` | HIGH | Update using statements |
| Builder API: lambda + `WithinDriver()` scoping | HIGH | Rewrite builder code |
| `Build()` returns `BuildResults` | HIGH | Access services from results |
| Docker Machine removed | HIGH | Use Docker Contexts |
| Docker Toolbox removed | HIGH | Use Docker Desktop |
| Commands namespace removed | HIGH | Use Driver Layer |
| Compose: struct-based arguments | MEDIUM | Update Compose calls |
| Legacy test packages removed | HIGH | Use `FluentDocker.Testing.*` adapters ([details](testing/migration-from-legacy.md)) |
| `FluentDockerTestBase` base class removed | HIGH | Use `XunitContainerFixture` / `MsTestResourceHelpers` / `NUnitResourceHelpers` (or generic `XunitResourceFixture<T>` / `CreateResourceAsync<T>`) |
| xUnit v3: `IAsyncLifetime` returns `ValueTask` | MEDIUM | Update `Task` → `ValueTask` |

## Step 1: Update NuGet Packages

```xml
<!-- OLD -->
<PackageReference Include="Ductus.FluentDocker" Version="2.*" />

<!-- NEW -->
<PackageReference Include="FluentDocker" Version="3.2.0-preview.2" />
```

## Step 2: Update Namespaces

```csharp
// OLD
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Model.Common;

// NEW
using FluentDocker.Builders;
using FluentDocker.Services;
using FluentDocker.Kernel;
```

**Automated fix:**
```bash
# Linux/macOS (sed -i.bak is portable across GNU and BSD sed; drop the backups after)
find . -name "*.cs" -exec sed -i.bak 's/Ductus\.FluentDocker/FluentDocker/g' {} \;
find . -name "*.cs.bak" -delete

# Windows PowerShell
Get-ChildItem -Recurse -Filter *.cs | ForEach-Object {
    (Get-Content $_.FullName) -replace 'Ductus\.FluentDocker', 'FluentDocker' | Set-Content $_.FullName
}
```

## Step 3: Create a Kernel

v3 requires a kernel with a registered driver before building containers.

```csharp
// NEW - Required kernel setup (multiple kernels per app/test session are supported)
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

// Explicit logger factory variant
await using var loggedKernel = await FluentDockerKernel.Create(loggerFactory)
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

## Step 4: Update Builder API

### Container Builder

```csharp
// OLD
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .ExposePort(80)
    .WaitForPort("80/tcp", 30000)
    .Build()
    .Start();

// NEW
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .Build();

var container = results.Containers.First();
```

### Network Builder

```csharp
// OLD
using var network = new Builder()
    .UseNetwork("my-network")
    .UseSubnet("10.18.0.0/16")
    .Build();

// NEW
using var nwResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("my-network")
        .WithSubnet("10.18.0.0/16"))
    .Build();

var network = nwResults.Networks.First();
```

### Volume Builder

```csharp
// OLD
using var vol = new Builder()
    .UseVolume("my-data")
    .Build();

// NEW
using var volResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v
        .WithName("my-data"))
    .Build();

var volume = volResults.Volumes.First();
```

### Compose Builder

```csharp
// OLD
using var svc = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .RemoveOrphans()
    .WaitForHttpUrl("http://localhost:8000/health")
    .Build()
    .Start();

// NEW
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans()
        .WithWait()
        .WithWaitTimeout(30))
    .Build();
```

### Image Builder

```csharp
// OLD
using var img = new Builder()
    .DefineImage("myapp:latest")
    .From("node:18-alpine")
    .Run("npm install")
    .ExposePorts(8080)
    .Command("node", "app.js")
    .Build();

// NEW
using var imgResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", img => img
        .From("node:18-alpine")
        .Run("npm install")
        .ExposePorts(8080)
        .Command("node", "app.js"))
    .Build();
```

## Step 5: Update Test Base Classes

The legacy `FluentDockerTestBase` (xUnit) and `FluentDockerTestBase` (MSTest) base
classes have been **removed**. Use the new adapter packages instead.

### xUnit — `XunitContainerFixture`

```csharp
// OLD
public class RedisFixture : FluentDockerTestBase
{
    protected override ContainerBuilder Build()
        => new Builder().UseContainer().UseImage("redis:alpine")
            .ExposePort(6379).WaitForPort("6379/tcp", 30000);
}

// NEW
public class RedisFixture : XunitContainerFixture
{
    public RedisFixture()
    {
        InitializeAsync(builder => builder
            .UseImage("redis:alpine")
            .ExposePort("6379")
            .WaitForPort("6379/tcp", 30000)
        ).GetAwaiter().GetResult();
    }
}
```

> **Tip:** For new code, prefer the `Configure(...)` pattern shown in
> [docs/testing/xunit.md](testing/xunit.md) instead of the sync-over-async
> constructor — it avoids deadlock risk in some sync contexts.

### MSTest — `MsTestResourceHelpers`

```csharp
// OLD
[TestClass]
public class RedisTests : FluentDockerTestBase
{
    protected override ContainerBuilder Build()
        => new Builder().UseContainer().UseImage("redis:alpine")
            .ExposePort(6379).WaitForPort("6379/tcp", 30000);
}

// NEW
[TestClass]
public class RedisTests
{
    private static FluentDockerKernel _kernel;
    private static ContainerResource _resource;

    [ClassInitialize]
    public static async Task ClassInit(TestContext ctx)
    {
        (_kernel, _resource) = await MsTestResourceHelpers.CreateContainerAsync(
            b => b.UseImage("redis:alpine").ExposePort("6379")
                  .WaitForPort("6379/tcp", 30000));
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
        => await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
}
```

See [Test Migration Guide](migrate-v2-to-v3/test-migration.md) for NUnit, Compose,
and collection fixture examples.

## Step 6: Remove Docker Machine Code

Docker Machine was deprecated by Docker and has been removed from v3.

```csharp
// OLD - Remove this code
var machines = new Hosts().Discover();
var machine = machines.First(x => x.Name == "default");

// NEW - Create kernel and use WithinDriver
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine"))
    .Build();
```

## Step 7: Update Compose Commands

Compose commands now use struct-based arguments:

```csharp
// OLD
host.ComposeBuild(altProjectName: "myproject", forceRm: true);
host.ComposeUp(composeFile: "docker-compose.yml", forceRecreate: true);
host.ComposeDown(removeOrphans: true, removeVolumes: true);

// NEW
await composeDriver.BuildAsync(context, new ComposeBuildConfig {
    ProjectName = "myproject",
    ForceRm = true
});

await composeDriver.UpAsync(context, new ComposeUpConfig {
    ComposeFiles = new List<string> { "docker-compose.yml" },
    ForceRecreate = true
});

await composeDriver.DownAsync(context, new ComposeDownConfig {
    RemoveOrphans = true,
    RemoveVolumes = true
});
```

## Step 8: Switch to Microsoft.Extensions.Logging

**BREAKING CHANGE in v3.0.0** — the static `Logging.Enabled()` /
`Logging.Disabled()` toggle and the `FluentDocker.Common.Logger` static class
are removed entirely. FluentDocker now logs through
`Microsoft.Extensions.Logging.Abstractions`. An `ILoggerFactory` is **required** by the
`KernelBuilder` constructor, but optional on the static `FluentDockerKernel.Create` factory.

Use `FluentDockerKernel.Create()` for the default `NullLoggerFactory`, or
`FluentDockerKernel.Create(factory)` when you want logs from a provider.

```csharp
// OLD (v2)
using Ductus.FluentDocker.Services;
Logging.Enabled();
Logging.Disabled();

// NEW (v3) — supply an ILoggerFactory to the kernel builder
using Microsoft.Extensions.Logging;
using FluentDocker.Kernel;

using var factory = LoggerFactory.Create(b => b.AddConsole());
await using var kernel = await FluentDockerKernel.Create(factory)
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

To suppress all logging (equivalent to v2's `Logging.Disabled()`), pass
`NullLoggerFactory.Instance` explicitly:

```csharp
using Microsoft.Extensions.Logging.Abstractions;

var silentKernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

The factory is automatically propagated through `DriverContext.LoggerFactory`
to all driver packs and component drivers, so third-party `IDriverPack`
implementations receive it without any interface change. Each FluentDocker type
uses its FQN as its log category for fine-grained filtering — see
[Utilities → Logging](utilities.md#logging) for details.

## Removed Features

### Docker Machine
- All `IMachineDriver` APIs removed
- Use Docker Contexts: `docker context create/use`

### Docker Toolbox
- `DOCKER_TOOLBOX_INSTALL_PATH` detection removed
- Use Docker Desktop for Windows/Mac

### docker-compose Binary
- v3 uses `docker compose` (Compose V2) automatically
- The standalone `docker-compose` binary is no longer supported

### Commands Namespace
- Replaced by Driver Layer architecture
- Commands are now internal implementation details

## New Features in v3.0.0

### Container Stats

```csharp
var stats = await container.GetStatsAsync();
Console.WriteLine($"CPU: {stats.Cpu.UsagePercent:F2}%");
Console.WriteLine($"Memory: {stats.Memory.Usage} bytes");
```

### Directory Copy

```csharp
// Copy entire directory to container
await container.CopyToAsync("/local/dir/", "/container/dir/");

// Copy from container
await container.CopyFromToPathAsync("/container/logs/", "/local/logs/");
```

### Static IPv4/IPv6

```csharp
using var nwResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("mynet")
        .WithSubnet("10.10.0.0/16"))
    .Build();

using var cResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithNetwork("mynet")
        .WithIPv4("10.10.0.100")
        .WithIPv6("2001:db8::100"))
    .Build();
```

### Full Async/Await

```csharp
// All service operations are now async
await container.StartAsync();
await container.StopAsync();
await container.ExecuteAsync("echo hello");
var stats = await container.GetStatsAsync();
```

## Migration Checklist

- [ ] Update NuGet packages
- [ ] Find & replace namespaces
- [ ] Add kernel creation
- [ ] Rewrite builder code to lambda + WithinDriver pattern
- [ ] Replace test base classes with adapter packages (see Step 5)
- [ ] Remove Docker Machine code
- [ ] Remove Docker Toolbox code
- [ ] Update Compose commands to struct-based
- [ ] Update logging configuration
- [ ] Run tests to verify
- [ ] Update CI/CD pipelines

## Detailed Migration Resources

For in-depth migration guidance, see these companion documents:

- [Complete API Mapping](migrate-v2-to-v3/api-mapping.md) — exhaustive v2 → v3 method and type mapping reference
- [Code Examples (Before/After)](migrate-v2-to-v3/code-examples.md) — side-by-side migration examples for common patterns
- [Test Migration Guide](migrate-v2-to-v3/test-migration.md) — xUnit, MSTest, and fixture migration patterns

### Optional: AI-agent automation

If you use an AI coding agent, the repository ships an **agent skill** (a prompt for the
agent, not human guidance) that automates much of the mechanical v2 → v3 rewrite. It lives
outside the documentation at
[`tools/claude-migration-skill.md`](https://github.com/mariotoffia/FluentDocker/blob/master/tools/claude-migration-skill.md);
copy it into your project's `.claude/skills/` and invoke `/migrate-v2-to-v3`. Always review
the agent's changes against the human guides above.

## Getting Help

- [Full Documentation](index.md)
- [GitHub Issues](https://github.com/mariotoffia/FluentDocker/issues)
