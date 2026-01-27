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
using FluentDocker.Model.Common;
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

## Step 3: Remove Docker Machine Code

Docker Machine was deprecated by Docker and has been removed from v3.

```csharp
// OLD - Remove this code
var machines = new Hosts().Discover();
var machine = machines.First(x => x.Name == "default");

// NEW - Use Docker Contexts instead
// Docker Desktop and native Docker don't need this
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .Build()
    .Start();
```

## Step 4: Update Compose Commands

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

## Step 5: Update Logging Configuration

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
Console.WriteLine($"CPU: {stats.CpuPercent:F2}%");
Console.WriteLine($"Memory: {stats.MemoryUsage} bytes");
```

### Directory Copy

```csharp
// Copy entire directory to container
await container.CopyToAsync("/local/dir/", "/container/dir/");

// Copy from container
await container.CopyFromAsync("/container/logs/", "/local/logs/");
```

### Static IPv4/IPv6

```csharp
using var network = new Builder()
    .UseNetwork("mynet")
    .UseSubnet("10.10.0.0/16")
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .UseNetwork(network)
    .UseIpV4("10.10.0.100")
    .UseIpV6("2001:db8::100")
    .Build()
    .Start();
```

### Full Async/Await

```csharp
// All service operations are now async
await container.StartAsync();
await container.StopAsync();
await container.ExecAsync("echo", "hello");
var stats = await container.GetStatsAsync();
```

## Migration Checklist

- [ ] Update NuGet packages
- [ ] Find & replace namespaces
- [ ] Remove Docker Machine code
- [ ] Remove Docker Toolbox code
- [ ] Update Compose commands to struct-based
- [ ] Update logging configuration
- [ ] Run tests to verify
- [ ] Update CI/CD pipelines

## Getting Help

- [Full Documentation](index.html)
- [GitHub Issues](https://github.com/mariotoffia/FluentDocker/issues)
- [Detailed Migration Guide](https://github.com/mariotoffia/FluentDocker/blob/master/MIGRATE_TO_V3.md)
