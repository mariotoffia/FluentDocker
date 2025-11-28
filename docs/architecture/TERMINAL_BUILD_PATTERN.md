# Terminal BuildAsync() Pattern - FluentDocker v3.0.0

## Overview

**Key Principles:**
1. `BuildAsync()` is terminal in ALL fluent APIs and returns `Task<TResult>`
2. All driver operations are asynchronous
3. Modern async/await pattern throughout

## Pattern Summary

### 1. Kernel Builder - Terminal BuildAsync()

**Before (nested builds - INCORRECT):**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker")
        .UseDockerCli()
        .Build()  // Builds driver
    .Build();  // Builds kernel
```

**After (terminal async build - CORRECT):**
```csharp
var kernel = await FluentDockerKernel.Create()
    .WithDriver("docker", d => d
        .UseDockerCli()
        .AtHost("unix:///var/run/docker.sock"))
    .BuildAsync();  // TERMINAL - returns Task<FluentDockerKernel>
```

### 2. Container Builder - Terminal BuildAsync()

**Before (Build returns Builder - INCORRECT):**
```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer()
            .UseImage("nginx")
            .Build()  // Returns Builder
    .GetResults();  // Get final results
```

**After (terminal async build - CORRECT):**
```csharp
var results = await new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer(c => c
            .UseImage("nginx")
            .WithName("web"))
    .BuildAsync();  // TERMINAL - returns Task<BuildResults>
```

## Benefits

1. **Async/await pattern** - Modern, non-blocking operations
2. **Simpler API** - Only one BuildAsync() call at the end
3. **Clear execution point** - BuildAsync() is when work happens
4. **Consistent pattern** - All fluent APIs work the same way
5. **Lambda configuration** - Clean, nested configuration syntax
6. **No GetResults()** - BuildAsync() returns results directly
7. **Parallel execution** - Multiple async operations can run concurrently

## Complete Example

```csharp
public async Task DeployMultiEnvironmentAsync()
{
    // Create kernel with multiple drivers
    var kernel = await FluentDockerKernel.Create()
        .WithDriver("docker-local", d => d
            .UseDockerCli()
            .AtHost("unix:///var/run/docker.sock"))
        .WithDriver("docker-remote", d => d
            .UseDockerApi()
            .AtHost("tcp://remote:2376")
            .WithCertificates("/path/to/certs")
            .AsDefault())
        .WithRetryPolicy(p => p
            .MaxAttempts(3)
            .ExponentialBackoff(2.0))
        .BuildAsync();  // TERMINAL - returns Task<FluentDockerKernel>

    // Deploy to multiple environments
    var deployment = await new Builder()
        .WithinDriver("docker-local", kernel)
            .UseContainer(c => c
                .UseImage("nginx")
                .WithName("local-web"))
            .UseContainer(c => c
                .UseImage("postgres:14")
                .WithName("local-db"))
        .WithinDriver("docker-remote")  // Reuses kernel
            .UseContainer(c => c
                .UseImage("myapp:v1.0")
                .WithName("prod-app"))
        .BuildAsync();  // TERMINAL - returns Task<BuildResults>

    // Access results
    var allServices = deployment.All;  // [local-web, local-db, prod-app]
    var localServices = deployment.ForDriver("docker-local");  // [local-web, local-db]
    var remoteServices = deployment.ForDriver("docker-remote");  // [prod-app]

    // Start services (async)
    foreach (var service in deployment.All)
    {
        await service.StartAsync();
    }

    // Cleanup (async disposal)
    await deployment.DisposeAllAsync();
}
```

## Migration Guide

**Rule:** Use async/await, remove nested `.Build()` calls, configure via lambdas.

**Kernel Migration:**
```csharp
// OLD (sync)
.WithDriver("id").UseDockerCli().Build()

// NEW (async with lambda)
await ...WithDriver("id", d => d.UseDockerCli()).BuildAsync()
```

**Container Migration:**
```csharp
// OLD (sync)
.UseContainer().UseImage("nginx").Build().GetResults()

// NEW (async with lambda)
await ...UseContainer(c => c.UseImage("nginx")).BuildAsync()
```

**Service Operations:**
```csharp
// OLD (sync)
container.Start();
container.Stop();
container.Remove();

// NEW (async)
await container.StartAsync();
await container.StopAsync();
await container.RemoveAsync();
```

**Driver Operations:**
```csharp
// OLD (sync)
var response = driver.Create(context, config);

// NEW (async)
var response = await driver.CreateAsync(context, config);
```

## Async/Await Pattern

**All operations in v3.0.0 are asynchronous.** See **ASYNC_OPERATIONS.md** for comprehensive async documentation including:

- Async driver interfaces (`Task<CommandResponse<T>>`)
- Async service operations (`StartAsync()`, `StopAsync()`, etc.)
- Cancellation support (`CancellationToken`)
- Progress reporting (`IProgress<T>`)
- Parallel execution patterns
- `IAsyncDisposable` implementation

**Quick example:**
```csharp
public async Task DeployAsync()
{
    var kernel = await FluentDockerKernel.Create()
        .WithDriver("docker", d => d.UseDockerCli())
        .BuildAsync();  // Async

    var deployment = await new Builder()
        .WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("nginx"))
        .BuildAsync();  // Async

    await deployment.All[0].StartAsync();  // Async
    await deployment.DisposeAllAsync();  // Async
}
```

---

## .NET SDK Version

**Target Framework:** .NET 10.0.100 (single framework targeting)
- **SDK Version:** 10.0.100
- **Single Target:** No multi-targeting like v2.x.x
- **Modern C# features:** Pattern matching, records, required members, async/await, etc.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```
