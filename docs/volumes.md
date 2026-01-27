---
layout: default
title: Volumes
nav_order: 6
---

# Volume Management

FluentDocker provides full support for Docker volumes, including named volumes, bind mounts, and tmpfs mounts.

## Named Volumes

### Create a Volume

```csharp
using FluentDocker.Builders;

using var volume = new Builder()
    .UseVolume("my-data")
    .Build();

Console.WriteLine($"Volume: {volume.Name}");
```

### Use Volume with Container

```csharp
using var volume = new Builder()
    .UseVolume("postgres-data")
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    .MountVolume(volume, "/var/lib/postgresql/data", MountType.ReadWrite)
    .WaitForPort("5432/tcp", 30000)
    .Build()
    .Start();

// Data persists in volume
```

### Reuse Existing Volume

```csharp
// First run - creates volume
using var volume1 = new Builder()
    .UseVolume("persistent-data")
    .Build();

using var container1 = new Builder()
    .UseContainer()
    .UseImage("alpine:latest")
    .MountVolume(volume1, "/data", MountType.ReadWrite)
    .Build()
    .Start();

// Write data
await container1.ExecAsync("sh", "-c", "echo 'Hello' > /data/test.txt");
container1.Dispose();

// Second run - reuses volume
using var volume2 = new Builder()
    .UseVolume("persistent-data")
    .ReuseIfExists()  // Don't recreate
    .Build();

using var container2 = new Builder()
    .UseContainer()
    .UseImage("alpine:latest")
    .MountVolume(volume2, "/data", MountType.ReadWrite)
    .Build()
    .Start();

// Read persisted data
var content = await container2.ExecAsync("cat", "/data/test.txt");
Console.WriteLine(content);  // "Hello"
```

## Bind Mounts

### Mount Host Directory

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .Mount("/local/html", "/usr/share/nginx/html", MountType.ReadOnly)
    .ExposePort(80)
    .Build()
    .Start();

// Changes to /local/html are immediately visible in container
```

### Read-Write Mount

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .Mount("/local/logs", "/app/logs", MountType.ReadWrite)
    .Build()
    .Start();

// App writes logs to /local/logs on host
```

### Mount Configuration Files

```csharp
using var container = new Builder()
    .UseContainer()
    .UseImage("nginx:alpine")
    .Mount("/local/nginx.conf", "/etc/nginx/nginx.conf", MountType.ReadOnly)
    .Mount("/local/ssl/", "/etc/ssl/certs/", MountType.ReadOnly)
    .ExposePort(443)
    .Build()
    .Start();
```

## Volume with TemplateString

```csharp
// Use dynamic paths
var tempPath = new TemplateString("${TEMP}/myapp/${RND}");

using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .Mount(tempPath, "/app/temp", MountType.ReadWrite)
    .Build()
    .Start();

// Mounts to something like /tmp/myapp/a1b2c3d4
```

## Volume Drivers

### Local Driver (Default)

```csharp
using var volume = new Builder()
    .UseVolume("local-vol")
    .UseDriver("local")
    .Build();
```

### NFS Volume

```csharp
using var volume = new Builder()
    .UseVolume("nfs-vol")
    .UseDriver("local")
    .UseDriverOption("type", "nfs")
    .UseDriverOption("o", "addr=192.168.1.100,rw")
    .UseDriverOption("device", ":/shared/data")
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .MountVolume(volume, "/data", MountType.ReadWrite)
    .Build()
    .Start();
```

### CIFS/SMB Volume

```csharp
using var volume = new Builder()
    .UseVolume("smb-vol")
    .UseDriver("local")
    .UseDriverOption("type", "cifs")
    .UseDriverOption("o", "username=user,password=pass")
    .UseDriverOption("device", "//server/share")
    .Build();
```

## Volume Labels

```csharp
using var volume = new Builder()
    .UseVolume("labeled-vol")
    .WithLabel("project", "myapp")
    .WithLabel("environment", "production")
    .Build();
```

## Multiple Volumes

```csharp
using var dataVolume = new Builder()
    .UseVolume("app-data")
    .Build();

using var logsVolume = new Builder()
    .UseVolume("app-logs")
    .Build();

using var configVolume = new Builder()
    .UseVolume("app-config")
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .MountVolume(dataVolume, "/app/data", MountType.ReadWrite)
    .MountVolume(logsVolume, "/app/logs", MountType.ReadWrite)
    .MountVolume(configVolume, "/app/config", MountType.ReadOnly)
    .Build()
    .Start();
```

## tmpfs Mounts

```csharp
// In-memory filesystem (fast, non-persistent)
using var container = new Builder()
    .UseContainer()
    .UseImage("myapp:latest")
    .UseTmpfs("/app/cache", "size=100m")  // 100MB tmpfs
    .Build()
    .Start();

// /app/cache is in memory - fast but lost on container stop
```

## Database Volume Examples

### PostgreSQL

```csharp
using var pgData = new Builder()
    .UseVolume("postgres-data")
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret", "PGDATA=/var/lib/postgresql/data/pgdata")
    .MountVolume(pgData, "/var/lib/postgresql/data", MountType.ReadWrite)
    .ExposePort(5432)
    .WaitForPort("5432/tcp", 30000)
    .Build()
    .Start();
```

### MySQL

```csharp
using var mysqlData = new Builder()
    .UseVolume("mysql-data")
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("mysql:8")
    .WithEnvironment("MYSQL_ROOT_PASSWORD=secret")
    .MountVolume(mysqlData, "/var/lib/mysql", MountType.ReadWrite)
    .ExposePort(3306)
    .WaitForPort("3306/tcp", 60000)
    .Build()
    .Start();
```

### MongoDB

```csharp
using var mongoData = new Builder()
    .UseVolume("mongo-data")
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("mongo:6")
    .WithEnvironment("MONGO_INITDB_ROOT_USERNAME=admin", "MONGO_INITDB_ROOT_PASSWORD=secret")
    .MountVolume(mongoData, "/data/db", MountType.ReadWrite)
    .ExposePort(27017)
    .WaitForPort("27017/tcp", 30000)
    .Build()
    .Start();
```

### Redis with Persistence

```csharp
using var redisData = new Builder()
    .UseVolume("redis-data")
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("redis:alpine")
    .UseCommand("redis-server", "--appendonly", "yes")
    .MountVolume(redisData, "/data", MountType.ReadWrite)
    .ExposePort(6379)
    .WaitForPort("6379/tcp", 30000)
    .Build()
    .Start();
```

## Development Workflow

### Hot Reload with Bind Mounts

```csharp
// Mount source code for hot reload
using var container = new Builder()
    .UseContainer()
    .UseImage("node:18-alpine")
    .Mount("/local/project/src", "/app/src", MountType.ReadWrite)
    .Mount("/local/project/package.json", "/app/package.json", MountType.ReadOnly)
    .UseWorkDir("/app")
    .UseCommand("npm", "run", "dev")
    .ExposePort(3000)
    .Build()
    .Start();

// Edit /local/project/src and see changes live
```

### Separate Build and Runtime

```csharp
// Build artifacts volume (shared between builds)
using var buildCache = new Builder()
    .UseVolume("build-cache")
    .Build();

// Node modules volume (faster than bind mount)
using var nodeModules = new Builder()
    .UseVolume("node-modules")
    .Build();

using var container = new Builder()
    .UseContainer()
    .UseImage("node:18-alpine")
    .Mount("/local/project", "/app", MountType.ReadWrite)
    .MountVolume(nodeModules, "/app/node_modules", MountType.ReadWrite)
    .MountVolume(buildCache, "/app/.cache", MountType.ReadWrite)
    .UseWorkDir("/app")
    .Build()
    .Start();
```

## Anonymous Volumes

```csharp
// Docker creates anonymous volume
using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")
    .WithEnvironment("POSTGRES_PASSWORD=secret")
    // PGDATA is defined in image, creates anonymous volume
    .Build()
    .Start();
```

## Volume Backup Example

```csharp
// Backup volume to tar file
using var dataVolume = new Builder()
    .UseVolume("app-data")
    .ReuseIfExists()
    .Build();

using var backup = new Builder()
    .UseContainer()
    .UseImage("alpine:latest")
    .MountVolume(dataVolume, "/data", MountType.ReadOnly)
    .Mount("/local/backups", "/backup", MountType.ReadWrite)
    .UseCommand("tar", "cvf", "/backup/data-backup.tar", "/data")
    .Build()
    .Start();

// Wait for completion
while (backup.State == ServiceRunningState.Running)
{
    await Task.Delay(100);
}

Console.WriteLine("Backup complete: /local/backups/data-backup.tar");
```

## Volume Inspection

```csharp
using var volume = new Builder()
    .UseVolume("inspect-vol")
    .Build();

var info = volume.GetConfiguration();
Console.WriteLine($"Name: {info.Name}");
Console.WriteLine($"Driver: {info.Driver}");
Console.WriteLine($"Mountpoint: {info.Mountpoint}");
Console.WriteLine($"Labels: {string.Join(", ", info.Labels ?? new Dictionary<string, string>())}");
```

## Cleanup

### Auto-cleanup on Dispose

```csharp
using var volume = new Builder()
    .UseVolume("temp-volume")
    .Build();

// Volume removed when disposed
```

### Keep Volume

```csharp
using var volume = new Builder()
    .UseVolume("persistent-volume")
    .KeepOnDispose()  // Don't remove
    .Build();

// Volume remains after disposal
```

### Manual Removal

```csharp
var volume = new Builder()
    .UseVolume("manual-volume")
    .Build();

// Use volume...

// Remove manually
volume.Remove();
```

## Testing with Volumes

```csharp
public class DatabaseTest : IDisposable
{
    private readonly IVolumeService _volume;
    private readonly IContainerService _db;

    public DatabaseTest()
    {
        var testId = Guid.NewGuid().ToString("N")[..8];

        _volume = new Builder()
            .UseVolume($"test-data-{testId}")
            .Build();

        _db = new Builder()
            .UseContainer()
            .WithName($"test-db-{testId}")
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .MountVolume(_volume, "/var/lib/postgresql/data", MountType.ReadWrite)
            .WaitForPort("5432/tcp", 30000)
            .Build()
            .Start();
    }

    [Fact]
    public async Task DataPersistsInVolume()
    {
        // Create table
        await _db.ExecAsync("psql", "-U", "postgres", "-c",
            "CREATE TABLE test (id SERIAL PRIMARY KEY, name TEXT);");

        // Insert data
        await _db.ExecAsync("psql", "-U", "postgres", "-c",
            "INSERT INTO test (name) VALUES ('Hello');");

        // Verify data
        var result = await _db.ExecAsync("psql", "-U", "postgres", "-t", "-c",
            "SELECT name FROM test WHERE id = 1;");

        Assert.Contains("Hello", result);
    }

    public void Dispose()
    {
        _db?.Dispose();
        _volume?.Dispose();
    }
}
```

## Next Steps

- [Images](images.html) - Building custom images
- [Containers](containers.html) - Container management
- [Networking](networking.html) - Custom networks
