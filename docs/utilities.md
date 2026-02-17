---
layout: default
title: Utilities
nav_order: 10
---

# Utilities

FluentDocker provides several utility classes and extension methods to simplify common operations.

## Step by Step

- Basics: [TemplateString](#templatestring), [HTTP Extensions (Wget)](#http-extensions-wget)
- Intermediate: [Resource Extensions](#resource-extensions), [Logging](#logging), [Model Extensions](#model-extensions)
- Advanced: [SudoMechanism](#sudomechanism), [Endpoint Resolution](#endpoint-resolution), [Command Response Handling](#command-response-handling), [Container Stats Parsing](#container-stats-parsing)

## TemplateString

Dynamic path interpolation with support for environment variables, temporary paths, and random strings.

### Basic Usage

```csharp
using FluentDocker.Model.Common;

// Temporary directory
var tempPath = new TemplateString("${TEMP}/myapp");
// Expands to: /tmp/myapp (Linux) or C:\Users\...\AppData\Local\Temp\myapp (Windows)

// With random suffix
var uniquePath = new TemplateString("${TEMP}/test-${RND}");
// Expands to: /tmp/test-a1b2c3d4

// Current directory
var workPath = new TemplateString("${PWD}/config");
// Expands to: /current/working/directory/config
```

### Environment Variables

```csharp
// Access any environment variable with E_ prefix
var homePath = new TemplateString("${E_HOME}/myapp");
// Expands to: /home/user/myapp

var userPath = new TemplateString("${E_USER}/.config");
// Expands to: username/.config

// Custom environment variables
Environment.SetEnvironmentVariable("MY_VAR", "custom-value");
var customPath = new TemplateString("${E_MY_VAR}/data");
// Expands to: custom-value/data
```

### Default Values

```csharp
// Default value if variable not set
var path = new TemplateString("${E_CUSTOM_PATH:-/default/path}");
// Expands to: /default/path (if CUSTOM_PATH not set)

var config = new TemplateString("${E_CONFIG_DIR:-${TEMP}/config}");
// Expands to: environment value or temp/config
```

### Supported Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `${TEMP}` | System temp directory | `/tmp` |
| `${TMP}` | Same as TEMP | `/tmp` |
| `${RND}` | Random 8-char hex string | `a1b2c3d4` |
| `${PWD}` | Current working directory | `/home/user/project` |
| `${E_*}` | Environment variable | `${E_HOME}` -> `/home/user` |

### With Containers

```csharp
// Create unique temp directory for test
var testDir = new TemplateString("${TEMP}/integration-test-${RND}");

using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .WithVolume(testDir, "/app/data"))
    .Build();
```

### Nested Templates

```csharp
var path = new TemplateString("${TEMP}/${E_USER:-anonymous}/session-${RND}");
// Might expand to: /tmp/john/session-deadbeef
```

## HTTP Extensions (Wget)

Simple HTTP operations for health checks and API testing.

### Basic GET Request

```csharp
using FluentDocker.Extensions;

// Simple GET
var response = await "http://localhost:8080/health".WgetAsync();
Console.WriteLine(response);  // Response body
```

### With Status Code

```csharp
var (statusCode, body) = await "http://localhost:8080/api/users".WgetWithStatusAsync();

if (statusCode == HttpStatusCode.OK)
{
    Console.WriteLine($"Users: {body}");
}
```

### Health Check Pattern

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapi:latest")
        .ExposePort("8080"))
    .Build();

var container = results.Containers.First();
var endpoint = container.ToHostExposedEndpoint("8080/tcp");
var healthUrl = $"http://localhost:{endpoint.Port}/health";

// Wait for healthy
for (int i = 0; i < 30; i++)
{
    try
    {
        var response = await healthUrl.WgetAsync();
        if (response.Contains("healthy"))
        {
            Console.WriteLine("Service is healthy!");
            break;
        }
    }
    catch
    {
        // Not ready yet
    }
    await Task.Delay(1000);
}
```

### Download File

```csharp
var url = new Uri("https://example.com/file.zip");
await url.DownloadAsync("/local/path/file.zip");
```

## Resource Extensions

Extract embedded resources from assemblies.

### Extract Single File

```csharp
using FluentDocker.Extensions;

// Extract embedded resource to temp directory
var configPath = typeof(MyTests).ResourceExtract(
    new TemplateString("${TEMP}/test-config"),
    "config.json"
);

// File is now at temp path
Console.WriteLine($"Config at: {configPath}");
```

### Extract Multiple Files

```csharp
// Extract multiple resources
typeof(MyTests).ResourceExtract(
    new TemplateString("${TEMP}/test-resources"),
    "config.json",
    "schema.sql",
    "test-data.csv"
);
```

### Query Resources

```csharp
// List all embedded resources
var resources = typeof(MyTests).ResourceQuery();
foreach (var resource in resources)
{
    Console.WriteLine($"Resource: {resource.Name}");
}
```

### Extract to File

```csharp
// Extract and get ResourceInfo
var resourceInfo = typeof(MyTests)
    .ResourceQuery()
    .Where(r => r.Name.EndsWith("config.json"))
    .ToFile(new TemplateString("${TEMP}/extracted"));
```

### With Containers

```csharp
// Extract test fixtures and mount in container
var fixturesPath = typeof(MyTests).ResourceExtract(
    new TemplateString("${TEMP}/fixtures-${RND}"),
    "test-data.sql",
    "seed-data.json"
);

using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .WithEnvironment("POSTGRES_PASSWORD=test")
        .WithVolume(fixturesPath, "/docker-entrypoint-initdb.d")
        .ExposePort("5432")
        .WaitForPort("5432/tcp", 30000))
    .Build();

// Database initialized with test-data.sql
```

## Logging

Control FluentDocker's logging output.

### Enable/Disable

```csharp
using FluentDocker.Common;

// Enable debug logging
Logging.Enabled();

// Disable logging
Logging.Disabled();
```

### Configure via appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "FluentDocker": "Debug"
    }
  }
}
```

### Log Levels

- **Trace**: Very detailed, includes command output
- **Debug**: Detailed operation info
- **Information**: Container start/stop events
- **Warning**: Non-fatal issues
- **Error**: Failures

## SudoMechanism

Configure sudo behavior for Linux environments via the kernel builder.

### No Sudo (Default)

```csharp
using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
// Commands run without sudo
```

### Passwordless Sudo

```csharp
using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d
        .WithSudo(SudoMechanism.NoPassword)
        .AsDefault())
    .BuildAsync();
// Commands prefixed with: sudo
```

### Sudo with Password

```csharp
using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d
        .WithSudo(SudoMechanism.Password, "your-password")
        .AsDefault())
    .BuildAsync();
// Commands prefixed with: echo 'password' | sudo -S
```

## Model Extensions

Useful extension methods for FluentDocker models.

### Get Exposed Endpoint

```csharp
using FluentDocker.Extensions;

var container = /* ... */;

// Get endpoint for exposed port
var endpoint = container.ToHostExposedEndpoint("8080/tcp");
Console.WriteLine($"Connect to: {endpoint.Address}:{endpoint.Port}");
```

### Get All Endpoints

```csharp
var ports = container.GetConfiguration().NetworkSettings.Ports;
foreach (var port in ports)
{
    Console.WriteLine($"Port: {port.Key} -> {port.Value?.FirstOrDefault()?.HostPort}");
}
```

### Check Container State

```csharp
if (container.State == ServiceRunningState.Running)
{
    // Container is running
}

// Or check configuration
var config = container.GetConfiguration(refresh: true);
if (config.State.Running)
{
    Console.WriteLine($"Container running since: {config.State.StartedAt}");
}
```

## Endpoint Resolution

Custom endpoint resolvers for special network configurations.

### Default Resolver

```csharp
// Uses container's exposed port mapping
var endpoint = container.ToHostExposedEndpoint("8080/tcp");
```

### Custom Resolver

```csharp
// For Docker Desktop on Mac/Windows
Func<IContainerService, string, IPEndPoint> customResolver = (container, port) =>
{
    var config = container.GetConfiguration();
    var portBinding = config.NetworkSettings.Ports[port]?.FirstOrDefault();

    if (portBinding != null)
    {
        // Force localhost for Docker Desktop
        return new IPEndPoint(IPAddress.Loopback, int.Parse(portBinding.HostPort));
    }

    return null;
};

// Use custom resolver
var endpoint = container.ToHostExposedEndpoint("8080/tcp", customResolver);
```

## Command Response Handling

Work with command results from Docker operations.

### Check Success

```csharp
var result = await containerDriver.CreateAsync(context, config);

if (result.Success)
{
    Console.WriteLine($"Container ID: {result.Data}");
}
else
{
    Console.WriteLine($"Error: {result.Error}");
    Console.WriteLine($"Exit code: {result.ExitCode}");
}
```

### Access Output

```csharp
var result = await containerDriver.ExecAsync(context, containerId, "ls", "-la");

if (result.Success)
{
    // Standard output
    foreach (var line in result.StdOut)
    {
        Console.WriteLine(line);
    }
}
else
{
    // Standard error
    foreach (var line in result.StdErr)
    {
        Console.Error.WriteLine(line);
    }
}
```

## Container Stats Parsing

Parse Docker stats output.

```csharp
var stats = await container.GetStatsAsync();

// CPU usage
Console.WriteLine($"CPU: {stats.CpuPercent:F2}%");

// Memory
Console.WriteLine($"Memory: {FormatBytes(stats.MemoryUsage)} / {FormatBytes(stats.MemoryLimit)}");
Console.WriteLine($"Memory %: {stats.MemoryPercent:F2}%");

// Network
Console.WriteLine($"Network RX: {FormatBytes(stats.NetworkRxBytes)}");
Console.WriteLine($"Network TX: {FormatBytes(stats.NetworkTxBytes)}");

// Block I/O
Console.WriteLine($"Block Read: {FormatBytes(stats.BlockReadBytes)}");
Console.WriteLine($"Block Write: {FormatBytes(stats.BlockWriteBytes)}");

string FormatBytes(long bytes)
{
    string[] sizes = { "B", "KB", "MB", "GB" };
    int order = 0;
    double len = bytes;
    while (len >= 1024 && order < sizes.Length - 1)
    {
        order++;
        len /= 1024;
    }
    return $"{len:0.##} {sizes[order]}";
}
```

## Utility Examples

### Test Data Generation

```csharp
public static class TestDataGenerator
{
    public static string UniqueId() => Guid.NewGuid().ToString("N")[..8];

    public static string TempPath(string prefix = "test") =>
        new TemplateString($"${{TEMP}}/{prefix}-${{RND}}").ToString();

    public static async Task<string> WaitForHealthy(string url, int timeoutSeconds = 30)
    {
        for (int i = 0; i < timeoutSeconds; i++)
        {
            try
            {
                var response = await url.WgetAsync();
                if (!string.IsNullOrEmpty(response))
                    return response;
            }
            catch { }
            await Task.Delay(1000);
        }
        throw new TimeoutException($"Service at {url} did not become healthy");
    }
}

// Usage
var testDir = TestDataGenerator.TempPath("integration");
var response = await TestDataGenerator.WaitForHealthy($"http://localhost:{port}/health");
```

### Container Factory

```csharp
public static class ContainerFactory
{
    public static BuildResults CreatePostgres(
        FluentDockerKernel kernel, string password = "test")
    {
        return new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c
                .UseImage("postgres:15-alpine")
                .WithEnvironment($"POSTGRES_PASSWORD={password}")
                .ExposePort("5432")
                .WaitForPort("5432/tcp", 30000))
            .Build();
    }

    public static BuildResults CreateRedis(FluentDockerKernel kernel)
    {
        return new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c
                .UseImage("redis:alpine")
                .ExposePort("6379")
                .WaitForPort("6379/tcp", 30000))
            .Build();
    }

    public static BuildResults CreateRabbitMQ(FluentDockerKernel kernel)
    {
        return new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c
                .UseImage("rabbitmq:3-management-alpine")
                .ExposePort("5672")
                .ExposePort("15672")
                .WaitForPort("5672/tcp", 60000))
            .Build();
    }
}

// Usage
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();

using var db = ContainerFactory.CreatePostgres(kernel);
using var cache = ContainerFactory.CreateRedis(kernel);
```

## Next Steps

- [Getting Started](getting-started.html) - Quick start guide
- [Containers](containers.html) - Container management
- [Testing](testing.html) - Test support
