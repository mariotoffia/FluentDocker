---
layout: default
title: Migration Guide
nav_order: 10
---

# Migrating to FluentDocker v3.0.0

This guide helps you migrate from v2.x.x to v3.0.0.

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

## Step 1: Update NuGet Packages

```xml
<!-- OLD -->
<PackageReference Include="Ductus.FluentDocker" Version="2.*" />

<!-- NEW -->
<PackageReference Include="FluentDocker" Version="3.*" />
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
# Linux/macOS
find . -name "*.cs" -exec sed -i '' 's/Ductus\.FluentDocker/FluentDocker/g' {} \;

# Windows PowerShell
Get-ChildItem -Recurse -Filter *.cs | ForEach-Object {
    (Get-Content $_.FullName) -replace 'Ductus\.FluentDocker', 'FluentDocker' | Set-Content $_.FullName
}
```

## Step 3: Create a Kernel

v3 requires a kernel with a registered driver before building containers.

```csharp
// NEW - Required kernel setup (multiple kernels per app/test session are supported)
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();

// Async variant
using var kernel = await FluentDockerKernel.Create()
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
    .WaitForHttp("web", "http://localhost:8000/health")
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

```csharp
// OLD
protected override ContainerBuilder Build()
{
    return new Builder()
        .UseContainer()
        .UseImage("redis:alpine")
        .ExposePort(6379)
        .WaitForPort("6379/tcp", 30000);
}

// NEW
protected override void ConfigureContainer(IContainerBuilder builder)
{
    builder
        .UseImage("redis:alpine")
        .ExposePort("6379")
        .WaitForPort("6379/tcp", 30000);
}
```

## Step 6: Remove Docker Machine Code

Docker Machine was deprecated by Docker and has been removed from v3.

```csharp
// OLD - Remove this code
var machines = new Hosts().Discover();
var machine = machines.First(x => x.Name == "default");

// NEW - Create kernel and use WithinDriver
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();

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

## Step 8: Update Logging Configuration

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
        .UseIpV4("10.10.0.100")
        .UseIpV6("2001:db8::100"))
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
- [ ] Update test base classes (ConfigureContainer)
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
- [Claude Code Migration Skill](migrate-v2-to-v3/claude-skill.md) — automated migration assistant (copy to `.claude/skills/` and invoke `/migrate-v2-to-v3`)

## Getting Help

- [Full Documentation](index.html)
- [GitHub Issues](https://github.com/mariotoffia/FluentDocker/issues)
