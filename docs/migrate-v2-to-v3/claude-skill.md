---
description: Migrate FluentDocker v2.x.x code to v3.0.0
# Installation: Copy this file to your project's .claude/skills/migrate-v2-to-v3.md
nav_exclude: true
search_exclude: true
---

# FluentDocker v2.x.x to v3.0.0 Migration Skill

When the user invokes this skill, perform the following migration steps on their
codebase. Work through each step in order. Report what you changed after each
step so the user can review incrementally.

**CRITICAL**: v2.x.x only supported Docker CLI. ALL migrations MUST use
`WithDockerCli` -- the kernel always registers a DockerCli driver.

---

## Step 1: Find all FluentDocker usage

Search the entire codebase for files that reference FluentDocker:

```
Grep for: Ductus\.FluentDocker
Grep for: FluentDocker\.Builders\.Builder
Grep for: using Ductus\.FluentDocker
```

List all files found. These are the files you will modify.

---

## Step 2: Update namespaces

Replace **all** occurrences of `Ductus.FluentDocker` with `FluentDocker` across
every `.cs` file in the project.

Common replacements:

| v2 Namespace | v3 Namespace |
|-------------|-------------|
| `Ductus.FluentDocker.Builders` | `FluentDocker.Builders` |
| `Ductus.FluentDocker.Services` | `FluentDocker.Services` |
| `Ductus.FluentDocker.Services.Extensions` | `FluentDocker.Services.Extensions` |
| `Ductus.FluentDocker.Model.Common` | `FluentDocker.Model.Common` |
| `Ductus.FluentDocker.Model.Containers` | `FluentDocker.Model.Containers` |
| `Ductus.FluentDocker.Model.Compose` | `FluentDocker.Model.Compose` |
| `Ductus.FluentDocker.Model.Images` | `FluentDocker.Model.Images` |
| `Ductus.FluentDocker.Model.Networks` | `FluentDocker.Model.Networks` |
| `Ductus.FluentDocker.Model.Volumes` | `FluentDocker.Model.Volumes` |
| `Ductus.FluentDocker.Commands` | **Removed** -- delete these imports |
| `Ductus.FluentDocker.MsTest` | `FluentDocker.Testing.MsTest` |
| `Ductus.FluentDocker.XUnit` | `FluentDocker.Testing.Xunit` |

Add these new imports where needed:

- `using FluentDocker.Kernel;` -- wherever kernel is created or referenced.
- `using FluentDocker.Services.Extensions;` -- wherever extension methods are
  used (`ToHostExposedEndpoint`, `GetConfiguration`, `Wget`).

---

## Step 3: Update NuGet package references

In all `.csproj` files, replace:

```xml
<!-- OLD -->
<PackageReference Include="Ductus.FluentDocker" Version="2.*" />
<PackageReference Include="Ductus.FluentDocker.MsTest" Version="2.*" />
<PackageReference Include="Ductus.FluentDocker.XUnit" Version="2.*" />

<!-- NEW -->
<PackageReference Include="FluentDocker" Version="3.*" />
<PackageReference Include="FluentDocker.Testing.MsTest" Version="3.*" />
<PackageReference Include="FluentDocker.Testing.Xunit" Version="3.*" />
```

---

## Step 4: Add kernel creation

Find the entry point, test setup, or fixture initialization. Add kernel creation
using the DockerCli driver (the only driver type available in v2):

### Synchronous context

```csharp
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();
```

### Async context (test fixtures, ASP.NET, etc.)

```csharp
var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

### Placement rules

- **xUnit IAsyncLifetime**: Create kernel in `InitializeAsync()`, store as field.
- **xUnit IClassFixture**: Use `XunitContainerFixture` -- kernel is managed automatically.
- **xUnit Collection Fixture**: Create kernel in the collection fixture, share
  across all classes.
- **MSTest**: Use `MsTestResourceHelpers.CreateContainerAsync()` -- kernel is returned
  in the tuple. Pass a `kernelFactory` for custom configuration.
- **NUnit**: Use `NUnitResourceHelpers.CreateContainerAsync()` -- same pattern as MSTest.
- **Application code**: Create once at startup, pass via DI or store as a field.

---

## Step 5: Transform builder patterns

### Container builder

```csharp
// OLD
var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .ExposePort(80)
    .WaitForPort("80/tcp", 30000)
    .Build()
    .Start();

// NEW
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .Build();

var container = results.Containers.First();
```

### Network builder

```csharp
// OLD
var network = new Builder()
    .UseNetwork("my-net")
    .UseSubnet("10.18.0.0/16")
    .Build();

// NEW
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("my-net")
        .WithSubnet("10.18.0.0/16"))
    .Build();

var network = results.Networks.First();
```

### Volume builder

```csharp
// OLD
var volume = new Builder()
    .UseVolume("my-data")
    .Build();

// NEW
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v
        .WithName("my-data"))
    .Build();

var volume = results.Volumes.First();
```

### Image builder

```csharp
// OLD
var img = new Builder()
    .DefineImage("myapp:latest")
    .From("node:18-alpine")
    .Run("npm install")
    .ExposePorts(8080)
    .Command("node", "app.js")
    .Build();

// NEW
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", img => img
        .From("node:18-alpine")
        .Run("npm install")
        .ExposePorts(8080)
        .Command("node", "app.js"))
    .Build();
```

### Compose builder

```csharp
// OLD
var svc = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .RemoveOrphans()
    .WaitForHttp("api", "http://localhost:8080/health")
    .Build()
    .Start();

// NEW
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans()
        .WithWait()
        .WithWaitTimeout(60))
    .Build();
```

### Key transformation rules

- `.Build().Start()` becomes just `.Build()` -- auto-starts.
- `Build()` return type changes from a service to `BuildResults`.
- Access services via `results.Containers.First()`, `results.Networks.First()`, etc.
- Port arguments change from `int` to `string` (e.g., `80` to `"80"`).
- Use async variants (`BuildAsync()`) in async contexts.

---

## Step 6: Fix renamed methods

| v2 Method | v3 Method |
|-----------|-----------|
| `container.Resume()` | `container.Start()` |
| `container.Pause()` | `await container.PauseAsync()` |
| `container.Logs()` | `await container.GetLogsAsync()` |
| `container.ExecAsync(...)` | `await container.ExecuteAsync(...)` |
| `container.CopyFromAsync(containerPath, hostPath)` | `await container.CopyFromToPathAsync(containerPath, hostPath)` |
| `container.CopyToAsync(hostPath, containerPath)` | `await container.CopyToAsync(hostPath, containerPath)` |
| `container.GetRunningProcesses()` | `await container.ExecuteAsync("ps", "-ef")` -- no direct equivalent; workaround is `ExecuteAsync` |
| `container.Export()` | `await container.ExportAsync()` |

---

## Step 7: Fix stats model

The container stats model was restructured:

| v2 Property | v3 Property |
|-------------|-------------|
| `stats.CpuPercent` | `stats.Cpu.UsagePercent` |
| `stats.MemoryUsage` | `stats.Memory.Usage` |
| `stats.MemoryLimit` | `stats.Memory.Limit` |
| `stats.MemoryPercent` | `stats.Memory.UsagePercent` |
| `stats.NetworkRx` | `stats.Network.RxBytes` |
| `stats.NetworkTx` | `stats.Network.TxBytes` |
| `stats.BlockRead` | `stats.Disk.ReadBytes` |
| `stats.BlockWrite` | `stats.Disk.WriteBytes` |

---

## Step 8: Fix SudoMechanism

The global static sudo configuration was removed. Configure sudo in the kernel
builder instead:

```csharp
// OLD
SudoMechanism.NoPassword.SetSudo();
var container = new Builder()
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start();

// NEW
var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d
        .WithSudo(SudoMechanism.NoPassword)
        .AsDefault())
    .Build();

var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c.UseImage("nginx"))
    .Build();
```

**Important**: The enum values are `SudoMechanism.None`, `SudoMechanism.NoPassword`,
and `SudoMechanism.Password`. There is no `SudoMechanism.Sudo` value.

---

## Step 9: Remove deleted APIs

### Docker Machine

Remove all Docker Machine code. It was deprecated by Docker and removed in v3.

```csharp
// DELETE all of these patterns:
var machines = new Hosts().Discover();
var machine = machines.First(x => x.Name == "default");
machine.Start();
machine.Create(1024, 20000, 1);
// Any IMachineDriver references
// Any using Ductus.FluentDocker.Machine imports
```

Replace with kernel creation:

```csharp
var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();
```

### Docker Toolbox

Remove `DOCKER_TOOLBOX_INSTALL_PATH` checks and `DockerToolbox` class usage.

### Commands namespace

The `Ductus.FluentDocker.Commands` namespace is removed entirely. All direct
command invocations are replaced by the driver layer:

```csharp
// OLD
using Ductus.FluentDocker.Commands;
host.InspectContainer(containerId);
host.ComposeUp(composeFile);

// NEW -- use driver layer
var driver = kernel.SysCtl<IContainerDriver>("docker");
await driver.InspectAsync(context, containerId);

// Or use the builder pattern (preferred)
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c.UseImage("nginx"))
    .Build();
```

---

## Step 10: Fix compose patterns

### Direct compose commands

```csharp
// OLD
host.ComposeUp(composeFile, forceRecreate: true);
host.ComposeDown(removeOrphans: true, removeVolumes: true);
host.ComposeBuild(altProjectName: "myproject", forceRm: true);

// NEW -- via builder (preferred)
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile(composeFile)
        .WithForceRecreate())
    .Build();

// NEW -- via driver (advanced)
var composeDriver = kernel.SysCtl<IComposeDriver>("docker");
await composeDriver.UpAsync(context, new ComposeUpConfig {
    ComposeFiles = new List<string> { composeFile },
    ForceRecreate = true
});
await composeDriver.DownAsync(context, new ComposeDownConfig {
    RemoveOrphans = true,
    RemoveVolumes = true
});
await composeDriver.BuildAsync(context, new ComposeBuildConfig {
    ProjectName = "myproject",
    ForceRm = true
});
```

### Compose wait strategies

v2 had per-service wait strategies (`.WaitForHttp()`, `.WaitForPort()`). v3 uses
Docker Compose V2's native `--wait` flag with healthchecks defined in the compose
file:

```csharp
// OLD
.WaitForHttp("api", "http://localhost:8080/health")

// NEW
.WithWait()          // Uses compose --wait flag
.WithWaitTimeout(60) // Timeout in seconds
```

For post-startup HTTP polling, access containers from `results.Containers`.

---

## Step 11: Fix dispose patterns

```csharp
// OLD: sync IDisposable
_container?.Dispose();

// NEW: async IAsyncDisposable -- dispose results BEFORE kernel
if (_results is IAsyncDisposable ad) await ad.DisposeAsync();
if (_kernel is IAsyncDisposable kd) await kd.DisposeAsync();

// OLD: using statement
using var container = new Builder().UseContainer().UseImage("nginx").Build().Start();

// NEW: await using
await using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault()).Build();
await using var results = new Builder()
    .WithinDriver("docker", kernel).UseContainer(c => c.UseImage("nginx")).Build();
```

---

## Step 12: Migrate test adapters

The legacy `FluentDockerTestBase` class has been removed. Use the new adapters:

- **MSTest**: `MsTestResourceHelpers.CreateContainerAsync(builder => ...)` returns
  `(kernel, resource)`. Cleanup with `MsTestResourceHelpers.DisposeAsync(resource, kernel)`.
- **xUnit**: Inherit `XunitContainerFixture` and call `InitializeAsync(builder => ...)`
  in the constructor.
- **NUnit**: `NUnitResourceHelpers.CreateContainerAsync(builder => ...)` -- same as MSTest.
- **Standalone xUnit IAsyncLifetime**: Create kernel + `BuildResults` fields, dispose
  results before kernel. Use concrete `BuildResults` class (no `IBuildResults` interface).

```csharp
// MSTest v3 pattern
(_kernel, _resource) = await MsTestResourceHelpers.CreateContainerAsync(
    builder => builder.UseImage("redis:alpine").ExposePort("6379"));

// xUnit v3 pattern
public class MyFixture : XunitContainerFixture
{
    public MyFixture()
    {
        InitializeAsync(builder => builder
            .UseImage("redis:alpine").WaitForPort("6379/tcp")
        ).GetAwaiter().GetResult();
    }
}
```

---

## Step 13: Update logging configuration

```json
// OLD
{
  "Logging": {
    "LogLevel": {
      "Ductus.FluentDocker": "Debug"
    }
  }
}

// NEW
{
  "Logging": {
    "LogLevel": {
      "FluentDocker": "Debug"
    }
  }
}
```

---

## Step 14: Verify the migration

After all changes:

1. Run `dotnet build` and fix any remaining compilation errors.
2. Run `dotnet test` and verify tests pass.
3. Check for any remaining `Ductus.FluentDocker` references.
4. Check for any remaining `.Build().Start()` chains.
5. Check for any remaining `ContainerBuilder Build()` overrides.

---

## Complete API Mapping Reference

### Namespace mapping

| v2 | v3 |
|----|-----|
| `Ductus.FluentDocker.Builders` | `FluentDocker.Builders` |
| `Ductus.FluentDocker.Services` | `FluentDocker.Services` |
| `Ductus.FluentDocker.Model.*` | `FluentDocker.Model.*` |
| `Ductus.FluentDocker.Commands` | Removed (use driver layer) |
| `Ductus.FluentDocker.MsTest` | `FluentDocker.Testing.MsTest` |
| `Ductus.FluentDocker.XUnit` | `FluentDocker.Testing.Xunit` |
| (new) | `FluentDocker.Kernel` |
| (new) | `FluentDocker.Services.Extensions` |

### Builder method mapping

| v2 | v3 |
|----|-----|
| `new Builder().UseContainer()` | `new Builder().WithinDriver("docker", kernel).UseContainer(c => ...)` |
| `.UseImage("x")` | `c.UseImage("x")` (inside lambda) |
| `.ExposePort(80)` | `c.ExposePort("80")` (string) |
| `.WaitForPort("80/tcp", ms)` | `c.WaitForPort("80/tcp", ms)` |
| `.Build().Start()` | `.Build()` or `.BuildAsync()` |
| `.UseCompose().FromFile("x")` | `.UseCompose(c => c.WithComposeFile("x"))` |
| `.UseNetwork("name")` | `.UseNetwork(n => n.WithName("name"))` |
| `.UseVolume("name")` | `.UseVolume(v => v.WithName("name"))` |
| `.DefineImage("tag")` | `.UseImage("tag", img => ...)` |

### Service method mapping

| v2 | v3 |
|----|-----|
| `container.Start()` | `await container.StartAsync()` |
| `container.Stop()` | `await container.StopAsync()` |
| `container.Resume()` | `container.Start()` (sync) or `await container.StartAsync()` |
| `container.Pause()` | `await container.PauseAsync()` |
| `container.Remove()` | `await container.RemoveAsync()` |
| `container.Logs()` | `await container.GetLogsAsync()` |
| `container.ExecAsync(...)` | `await container.ExecuteAsync(...)` |
| `container.CopyFromAsync(a, b)` | `await container.CopyFromToPathAsync(a, b)` |
| `container.GetRunningProcesses()` | `await container.ExecuteAsync("ps", "-ef")` -- no direct equivalent; workaround is `ExecuteAsync` |
| `container.Export()` | `await container.ExportAsync()` |

### Stats model mapping

| v2 | v3 |
|----|-----|
| `stats.CpuPercent` | `stats.Cpu.UsagePercent` |
| `stats.MemoryUsage` | `stats.Memory.Usage` |
| `stats.MemoryLimit` | `stats.Memory.Limit` |
| `stats.MemoryPercent` | `stats.Memory.UsagePercent` |
| `stats.NetworkRx` | `stats.Network.RxBytes` |
| `stats.NetworkTx` | `stats.Network.TxBytes` |
| `stats.BlockRead` | `stats.Disk.ReadBytes` |
| `stats.BlockWrite` | `stats.Disk.WriteBytes` |

### Compose method mapping

| v2 | v3 Builder |
|----|-----------|
| `.FromFile("x")` | `.WithComposeFile("x")` |
| `.RemoveOrphans()` | `.WithRemoveOrphans()` |
| `.WaitForHttp("svc", "url")` | `.WithWait()` + compose healthchecks |
| `.ForceRecreate()` | `.WithForceRecreate()` |
| `.Build().Start()` | `.Build()` (auto-starts) |
| `host.ComposeUp(...)` | `composeDriver.UpAsync(ctx, config)` |
| `host.ComposeDown(...)` | `composeDriver.DownAsync(ctx, config)` |
| `host.ComposeBuild(...)` | `composeDriver.BuildAsync(ctx, config)` |

### Removed APIs (no v3 equivalent)

| v2 API | Reason |
|--------|--------|
| `new Hosts().Discover()`, `IMachineDriver` | Docker Machine removed |
| `DockerToolbox`, `DOCKER_TOOLBOX_INSTALL_PATH` | Docker Toolbox removed |
| `Ductus.FluentDocker.Commands.*` | Replaced by driver layer |
| `SudoMechanism.SetSudo()` | Configure via kernel `.WithSudo()` |
| `docker-compose` (V1 binary) | V3 uses `docker compose` (V2) |

---

## Step 15: Migrate test support packages

Legacy test packages have been removed. Use the new packages:

| Legacy | New Package |
|---|---|
| `Ductus.FluentDocker.XUnit` | `FluentDocker.Testing.Xunit` |
| `Ductus.FluentDocker.MsTest` | `FluentDocker.Testing.MsTest` |
| (none) | `FluentDocker.Testing.NUnit` |

Key changes:
- xUnit: `FluentDockerTestBase` → `XunitContainerFixture` with `InitializeAsync` lambda
- MSTest: `FluentDockerTestBase` → `MsTestResourceHelpers.CreateContainerAsync` static helper
- `PostgresTestBase` → configure container directly or use plugin package
- New core namespace: `FluentDocker.Testing.Core` (inside main assembly)
- Plugin system: `FluentDocker.Testing.Core.Plugins`

See `docs/testing/migration-from-legacy.md` for complete side-by-side examples.
