---
layout: default
title: Volumes
nav_order: 6
---

# Volume Management

FluentDocker v3 provides full support for Docker volumes, including named volumes and bind mounts.
All operations go through the kernel and driver-scoped builder pattern.

## Kernel Setup

Before using any builder, create a kernel once per application:

```csharp
using FluentDocker.Kernel;
using FluentDocker.Builders;

// Create once, reuse everywhere
var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();
```

## Named Volumes

### Create a Volume

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("my-data"))
    .Build();

var volume = results.Volumes.First();
Console.WriteLine($"Volume: {volume.VolumeName}");
```

### Use Volume with Container

Create the volume and container in separate builders. Reference the volume by its
name string in `.WithVolume()`:

```csharp
// Step 1: Create the volume
using var volResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("postgres-data"))
    .Build();

// Step 2: Create container referencing volume by name
using var ctrResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD", "secret")
        .WithVolume("postgres-data", "/var/lib/postgresql/data")
        .WaitForPort("5432/tcp", 30000))
    .Build();

var container = ctrResults.Containers.First();
// Data persists in volume
```

### Reuse Existing Volume

Named volumes are reused by default when the same name already exists in Docker.
There is no need for an explicit "reuse" flag.

```csharp
// First run - creates volume and writes data
using var volResults1 = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("persistent-data"))
    .Build();

using var ctrResults1 = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("alpine:latest")
        .WithVolume("persistent-data", "/data"))
    .Build();

var container1 = ctrResults1.Containers.First();
await container1.ExecuteAsync("sh -c 'echo Hello > /data/test.txt'");
container1.Dispose();

// Second run - volume already exists and is reused automatically
using var volResults2 = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("persistent-data"))
    .Build();

using var ctrResults2 = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("alpine:latest")
        .WithVolume("persistent-data", "/data"))
    .Build();

var container2 = ctrResults2.Containers.First();
var content = await container2.ExecuteAsync("cat /data/test.txt");
Console.WriteLine(content);  // "Hello"
```

## Bind Mounts

### Mount Host Directory

Bind mounts use the same `.WithVolume()` method. When the first argument is a
host filesystem path (rather than a volume name), Docker creates a bind mount.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithVolume("/local/html", "/usr/share/nginx/html")
        .ExposePort("80"))
    .Build();

// Changes to /local/html are immediately visible in container
```

### Mount Configuration Files

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithVolume("/local/nginx.conf", "/etc/nginx/nginx.conf")
        .WithVolume("/local/ssl/", "/etc/ssl/certs/")
        .ExposePort("443"))
    .Build();
```

## Volume Drivers

### Local Driver (Default)

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v
        .WithName("local-vol")
        .UseDriver("local"))
    .Build();
```

### NFS Volume

```csharp
using var volResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v
        .WithName("nfs-vol")
        .UseDriver("local")
        .WithDriverOption("type", "nfs")
        .WithDriverOption("o", "addr=192.168.1.100,rw")
        .WithDriverOption("device", ":/shared/data"))
    .Build();

using var ctrResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .WithVolume("nfs-vol", "/data"))
    .Build();
```

### CIFS/SMB Volume

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v
        .WithName("smb-vol")
        .UseDriver("local")
        .WithDriverOption("type", "cifs")
        .WithDriverOption("o", "username=user,password=pass")
        .WithDriverOption("device", "//server/share"))
    .Build();
```

## Volume Labels

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v
        .WithName("labeled-vol")
        .WithLabel("project", "myapp")
        .WithLabel("environment", "production"))
    .Build();
```

## Multiple Volumes

```csharp
// Create all volumes
using var volResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("app-data"))
    .UseVolume(v => v.WithName("app-logs"))
    .UseVolume(v => v.WithName("app-config"))
    .Build();

// Create container referencing volumes by name
using var ctrResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .WithVolume("app-data", "/app/data")
        .WithVolume("app-logs", "/app/logs")
        .WithVolume("app-config", "/app/config"))
    .Build();
```

## Database Volume Examples

### PostgreSQL

```csharp
using var volResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("postgres-data"))
    .Build();

using var ctrResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD", "secret")
        .WithEnvironment("PGDATA", "/var/lib/postgresql/data/pgdata")
        .WithVolume("postgres-data", "/var/lib/postgresql/data")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000))
    .Build();
```

### MySQL

```csharp
using var volResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("mysql-data"))
    .Build();

using var ctrResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("mysql:8")
        .WithEnvironment("MYSQL_ROOT_PASSWORD", "secret")
        .WithVolume("mysql-data", "/var/lib/mysql")
        .ExposePort("3306")
        .WaitForPort("3306/tcp", 60000))
    .Build();
```

### MongoDB

```csharp
using var volResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("mongo-data"))
    .Build();

using var ctrResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("mongo:6")
        .WithEnvironment("MONGO_INITDB_ROOT_USERNAME", "admin")
        .WithEnvironment("MONGO_INITDB_ROOT_PASSWORD", "secret")
        .WithVolume("mongo-data", "/data/db")
        .ExposePort("27017")
        .WaitForPort("27017/tcp", 30000))
    .Build();
```

### Redis with Persistence

```csharp
using var volResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("redis-data"))
    .Build();

using var ctrResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("redis:alpine")
        .WithCommand("redis-server", "--appendonly", "yes")
        .WithVolume("redis-data", "/data")
        .ExposePort("6379")
        .WaitForPort("6379/tcp", 30000))
    .Build();
```

## Development Workflow

### Hot Reload with Bind Mounts

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("node:18-alpine")
        .WithVolume("/local/project/src", "/app/src")
        .WithVolume("/local/project/package.json", "/app/package.json")
        .WithWorkingDirectory("/app")
        .WithCommand("npm", "run", "dev")
        .ExposePort("3000"))
    .Build();

// Edit /local/project/src and see changes live
```

### Separate Build and Runtime

```csharp
// Create cache volumes
using var volResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("build-cache"))
    .UseVolume(v => v.WithName("node-modules"))
    .Build();

// Create container with bind mount for source and named volumes for caches
using var ctrResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("node:18-alpine")
        .WithVolume("/local/project", "/app")
        .WithVolume("node-modules", "/app/node_modules")
        .WithVolume("build-cache", "/app/.cache")
        .WithWorkingDirectory("/app"))
    .Build();
```

## Volume Backup Example

```csharp
using var volResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("app-data"))
    .Build();

using var ctrResults = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("alpine:latest")
        .WithVolume("app-data", "/data")
        .WithVolume("/local/backups", "/backup")
        .WithCommand("tar", "cvf", "/backup/data-backup.tar", "/data"))
    .Build();

var backup = ctrResults.Containers.First();

// Wait for completion
while (backup.State == ServiceRunningState.Running)
{
    await Task.Delay(100);
}

Console.WriteLine("Backup complete: /local/backups/data-backup.tar");
```

## Volume Inspection

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("inspect-vol"))
    .Build();

var volume = results.Volumes.First();
var info = await volume.InspectAsync();
Console.WriteLine($"Name: {info.Name}");
Console.WriteLine($"Driver: {info.Driver}");
Console.WriteLine($"Scope: {info.Scope}");
Console.WriteLine($"Created: {info.Created}");
```

## Cleanup

### Auto-cleanup with RemoveOnDispose

By default, volumes are kept when the `BuildResults` is disposed. Use
`RemoveOnDispose()` to automatically remove the volume on disposal.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v
        .WithName("temp-volume")
        .RemoveOnDispose())
    .Build();

// Volume is removed when results is disposed
```

### Keep Volume (Default Behavior)

Volumes are kept after disposal by default. No special flag is needed.

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("persistent-volume"))
    .Build();

// Volume remains after disposal (default behavior)
```

### Manual Removal

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseVolume(v => v.WithName("manual-volume"))
    .Build();

var volume = results.Volumes.First();

// Use volume...

// Remove manually
await volume.RemoveAsync();
```

## Testing with Volumes

```csharp
public class DatabaseTest : IAsyncDisposable
{
    private readonly BuildResults _volResults;
    private readonly BuildResults _ctrResults;

    public DatabaseTest()
    {
        var kernel = FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .Build();

        var testId = Guid.NewGuid().ToString("N")[..8];

        _volResults = new Builder()
            .WithinDriver("docker", kernel)
            .UseVolume(v => v
                .WithName($"test-data-{testId}")
                .RemoveOnDispose())
            .Build();

        _ctrResults = new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c
                .WithName($"test-db-{testId}")
                .UseImage("postgres:15-alpine")
                .WithEnvironment("POSTGRES_PASSWORD", "test")
                .WithVolume($"test-data-{testId}", "/var/lib/postgresql/data")
                .WaitForPort("5432/tcp", 30000))
            .Build();
    }

    [Fact]
    public async Task DataPersistsInVolume()
    {
        var db = _ctrResults.Containers.First();

        // Create table
        await db.ExecuteAsync(
            "psql -U postgres -c 'CREATE TABLE test (id SERIAL PRIMARY KEY, name TEXT);'");

        // Insert data
        await db.ExecuteAsync(
            "psql -U postgres -c \"INSERT INTO test (name) VALUES ('Hello');\"");

        // Verify data
        var result = await db.ExecuteAsync(
            "psql -U postgres -t -c 'SELECT name FROM test WHERE id = 1;'");

        Assert.Contains("Hello", result);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctrResults.DisposeAllAsync();
        await _volResults.DisposeAllAsync();
    }
}
```

## Note on tmpfs Mounts

The v3 `IContainerBuilder` does not include a tmpfs mount method. If you need
tmpfs mounts, configure them directly through Docker run flags or use a
`docker-compose.yml` file via the Compose builder.

## Next Steps

- [Images](images.html) - Building custom images
- [Containers](containers.html) - Container management
- [Networking](networking.html) - Custom networks
