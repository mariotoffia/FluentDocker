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
// Expands to: /tmp/test-tmpk4xz0f.tmp

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

### Custom Environment Variables

```csharp
// If the environment variable is not set, the token remains unexpanded
var path = new TemplateString("${E_CUSTOM_PATH}");
// Expands to: value of CUSTOM_PATH env var, or literal "${E_CUSTOM_PATH}" if unset

// Combine with other variables
var config = new TemplateString("${E_CONFIG_DIR}/data");
// Expands to: <CONFIG_DIR value>/data
```

### Supported Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `${TEMP}` | System temp directory | `/tmp` |
| `${TMP}` | Same as TEMP | `/tmp` |
| `${RND}` | Random filename via `Path.GetRandomFileName()` | `tmpk4xz0f.tmp` |
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

### Combined Variables

```csharp
var path = new TemplateString("${TEMP}/${E_USER}/session-${RND}");
// Might expand to: /tmp/john/session-tmpk4xz0f.tmp
```

## HTTP Extensions (Wget)

Simple HTTP operations for health checks and API testing.

### Basic GET Request

```csharp
using FluentDocker.Extensions;

// Simple GET
var response = await "http://localhost:8080/health".Wget();
Console.WriteLine(response);  // Response body
```

### Full Request with Status Code

```csharp
using FluentDocker.Extensions;

// DoRequest returns a RequestResponse struct with Code, Body, Headers, Err
var result = await "http://localhost:8080/api/users".DoRequest();

if (result.Code == HttpStatusCode.OK)
{
    Console.WriteLine($"Users: {result.Body}");
}

// POST with JSON body
var postResult = await "http://localhost:8080/api/users".DoRequest(
    method: HttpMethod.Post,
    contentType: "application/json",
    body: "{\"name\":\"test\"}");
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
        var response = await healthUrl.Wget();
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
await url.Download("/local/path/file.zip");
```

## Resource Extensions

Extract embedded resources from assemblies.

### Extract Single File

```csharp
using FluentDocker.Extensions;

// Extract embedded resource to temp directory (returns void)
typeof(MyTests).ResourceExtract(
    new TemplateString("${TEMP}/test-config"),
    "config.json"
);

// File is now at: ${TEMP}/test-config/config.json
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
// Extract matching resources to a directory (returns void)
typeof(MyTests)
    .ResourceQuery()
    .Where(r => r.Name.EndsWith("config.json"))
    .ToFile(new TemplateString("${TEMP}/extracted"));
```

### With Containers

```csharp
// Extract test fixtures and mount in container
var fixturesPath = new TemplateString("${TEMP}/fixtures-${RND}");
typeof(MyTests).ResourceExtract(
    fixturesPath,
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
using FluentDocker.Services;

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
using FluentDocker.Model.Common;

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
using FluentDocker.Services.Extensions;

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
var config = container.GetConfiguration(fresh: true);
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

The custom resolver is set via `UseCustomResolver()` on the container builder, not passed
to `ToHostExposedEndpoint()`. The resolver signature is:
`Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint>`

```csharp
using FluentDocker.Model.Containers;

// Configure custom resolver on the builder
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .ExposePort("8080")
        .UseCustomResolver((portBindings, portAndProto, requestUrl) =>
        {
            if (portBindings.TryGetValue(portAndProto, out var endpoints)
                && endpoints.Length > 0)
            {
                // Force localhost for Docker Desktop on Mac/Windows
                return new IPEndPoint(IPAddress.Loopback, endpoints[0].Port);
            }

            return null;
        }))
    .Build();

// ToHostExposedEndpoint uses the custom resolver automatically
var endpoint = results.Containers.First().ToHostExposedEndpoint("8080/tcp");
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
    Console.WriteLine(result.Data);          // Typed payload
    Console.WriteLine(result.Output);        // Combined stdout (string)
}
else
{
    Console.WriteLine($"{result.ErrorCode}: {result.Error}");
    var ctx = result.ErrorContext;
    Console.WriteLine($"stdout:\n{ctx?.StdOut}\nstderr:\n{ctx?.StdErr}");
}
```

## Container Stats Parsing

Parse Docker stats output.

```csharp
var stats = await container.GetStatsAsync();

// CPU usage
Console.WriteLine($"CPU: {stats.Cpu.UsagePercent:F2}%");

// Memory
Console.WriteLine($"Memory: {FormatBytes(stats.Memory.Usage)} / {FormatBytes(stats.Memory.Limit)}");
Console.WriteLine($"Memory %: {stats.Memory.UsagePercent:F2}%");

// Network
Console.WriteLine($"Network RX: {FormatBytes(stats.Network.RxBytes)}");
Console.WriteLine($"Network TX: {FormatBytes(stats.Network.TxBytes)}");

// Block I/O
Console.WriteLine($"Block Read: {FormatBytes(stats.Disk.ReadBytes)}");
Console.WriteLine($"Block Write: {FormatBytes(stats.Disk.WriteBytes)}");

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
                var response = await url.Wget();
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
