# Terminal Build() Pattern - FluentDocker v3.0.0

## Overview

**Key Principle:** `Build()` is terminal in ALL fluent APIs. It executes all queued operations and returns the final result.

## Pattern Summary

### 1. Kernel Builder - Terminal Build()

**Before (nested builds - INCORRECT):**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker")
        .UseDockerCli()
        .Build()  // Builds driver
    .Build();  // Builds kernel  
```

**After (terminal build -CORRECT):**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker", d => d
        .UseDockerCli()
        .AtHost("unix:///var/run/docker.sock"))
    .Build();  // TERMINAL - returns FluentDockerKernel
```

### 2. Container Builder - Terminal Build()

**Before (Build returns Builder - INCORRECT):**
```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer()
            .UseImage("nginx")
            .Build()  // Returns Builder
    .GetResults();  // Get final results
```

**After (terminal build - CORRECT):**
```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer(c => c
            .UseImage("nginx")
            .WithName("web"))
    .Build();  // TERMINAL - returns BuildResults
```

## Benefits

1. **Simpler API** - Only one Build() call at the end
2. **Clear execution point** - Build() is when work happens
3. **Consistent pattern** - All fluent APIs work the same way
4. **Lambda configuration** - Clean, nested configuration syntax
5. **No GetResults()** - Build() returns results directly

## Complete Example

```csharp
// Create kernel with multiple drivers
var kernel = FluentDockerKernel.Create()
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
    .Build();  // TERMINAL - returns FluentDockerKernel

// Deploy to multiple environments
var deployment = new Builder()
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
    .Build();  // TERMINAL - returns BuildResults

// Access results
var allServices = deployment.All;  // [local-web, local-db, prod-app]
var localServices = deployment.ForDriver("docker-local");  // [local-web, local-db]
var remoteServices = deployment.ForDriver("docker-remote");  // [prod-app]

// Cleanup
deployment.DisposeAll();
```

## Migration Guide

**Rule:** Remove all nested `.Build()` calls and configure via lambdas instead.

**Kernel Migration:**
```csharp
// OLD
.WithDriver("id").UseDockerCli().Build()

// NEW  
.WithDriver("id", d => d.UseDockerCli())
```

**Container Migration:**
```csharp
// OLD
.UseContainer().UseImage("nginx").Build().GetResults()

// NEW
.UseContainer(c => c.UseImage("nginx")).Build()
```

## .NET SDK Version

**Target Framework:** .NET 10.0.100 (single framework targeting)
- **SDK Version:** 10.0.100
- **Single Target:** No multi-targeting like v2.x.x
- **Modern C# features:** Pattern matching, records, required members, etc.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```
