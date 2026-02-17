---
layout: default
title: Error Handling
nav_order: 12
description: "FluentDocker v3.0 error handling - Exception hierarchy, error codes, diagnostics"
---

# Error Handling

FluentDocker v3.0 introduces a comprehensive error handling system with typed exceptions, error codes, and rich diagnostic context.

## Step by Step

- Basics: [Overview](#overview), [Exception Hierarchy](#exception-hierarchy), [Error Codes](#error-codes)
- Intermediate: [Error Context](#error-context), [Migration Examples](#migration-examples), [Exception Type Mapping](#exception-type-mapping)
- Advanced: [Retry Support](#retry-support), [Best Practices](#best-practices)

## Overview

**v2.x.x Limitations:**
- Only 2 exception types (`FluentDockerException`, `FluentDockerNotSupportedException`)
- No error codes - string parsing required
- No diagnostic context
- Minimal logging

**v3.0.0 Improvements:**
- **20+ typed exceptions** for precise error handling
- **Hierarchical error codes** for programmatic decisions
- **Rich error context** with diagnostics
- **IsTransient flag** for retry decisions
- **Built-in retry mechanisms**

---

## Exception Hierarchy

### Base Exception

```csharp
public class FluentDockerException : Exception
{
    // Error code for programmatic identification
    public string ErrorCode { get; set; }

    // Diagnostic context
    public ErrorContext Context { get; set; }

    // Whether this error can be retried
    public bool IsTransient { get; set; }

    // HTTP status code (for API drivers)
    public int? StatusCode { get; set; }
}
```

### Driver Exceptions

| Exception | Error Code | When Thrown |
|-----------|------------|-------------|
| `DriverException` | `DRIVER.GENERAL` | Base driver error |
| `DriverNotFoundException` | `DRIVER.NOT_FOUND` | Driver ID not registered |
| `DriverNotAvailableException` | `DRIVER.NOT_AVAILABLE` | Daemon down, binary missing |
| `DriverHealthCheckException` | `DRIVER.HEALTH_CHECK_FAILED` | Health check failed |
| `DriverRegistrationException` | `DRIVER.REGISTRATION_FAILED` | Registration failed |
| `DriverSelectionException` | `DRIVER.SELECTION_FAILED` | No suitable driver found |

### Container Exceptions

| Exception | Error Code | When Thrown |
|-----------|------------|-------------|
| `ContainerException` | `CONTAINER.GENERAL` | Base container error |
| `ContainerNotFoundException` | `CONTAINER.NOT_FOUND` | Container doesn't exist |
| `ContainerCreationException` | `CONTAINER.CREATION_FAILED` | Create failed |
| `ContainerStartException` | `CONTAINER.START_FAILED` | Start failed |
| `ContainerStopException` | `CONTAINER.STOP_FAILED` | Stop failed |
| `InvalidContainerStateException` | `CONTAINER.INVALID_STATE` | Wrong state for operation |

### Image Exceptions

| Exception | Error Code | When Thrown |
|-----------|------------|-------------|
| `ImageException` | `IMAGE.GENERAL` | Base image error |
| `ImageNotFoundException` | `IMAGE.NOT_FOUND` | Image doesn't exist |
| `ImagePullException` | `IMAGE.PULL_FAILED` | Pull failed (often transient) |
| `ImageBuildException` | `IMAGE.BUILD_FAILED` | Build failed |

### Network & Volume Exceptions

| Exception | Error Code | When Thrown |
|-----------|------------|-------------|
| `NetworkException` | `NETWORK.GENERAL` | Base network error |
| `NetworkNotFoundException` | `NETWORK.NOT_FOUND` | Network doesn't exist |
| `VolumeException` | `VOLUME.GENERAL` | Base volume error |
| `VolumeNotFoundException` | `VOLUME.NOT_FOUND` | Volume doesn't exist |

### Compose Exceptions

| Exception | Error Code | When Thrown |
|-----------|------------|-------------|
| `ComposeException` | `COMPOSE.GENERAL` | Base compose error |
| `ComposeFileNotFoundException` | `COMPOSE.FILE_NOT_FOUND` | Compose file missing |
| `ComposeValidationException` | `COMPOSE.VALIDATION_FAILED` | Invalid compose file |
| `ComposeUpException` | `COMPOSE.UP_FAILED` | Compose up failed |

### Validation Exceptions

| Exception | Error Code | When Thrown |
|-----------|------------|-------------|
| `ConfigurationException` | `CONFIGURATION.GENERAL` | Config error |
| `ValidationException` | `VALIDATION.FAILED` | Validation failed |

---

## Error Codes

Error codes follow a hierarchical pattern: `CATEGORY.SUBCATEGORY`:

```csharp
public static class ErrorCodes
{
    public const string NotSupported = "NOT_SUPPORTED";
    public const string Unknown = "UNKNOWN";
    public const string Timeout = "TIMEOUT";

    public static class Driver
    {
        public const string General = "DRIVER.GENERAL";
        public const string NotFound = "DRIVER.NOT_FOUND";
        public const string NotAvailable = "DRIVER.NOT_AVAILABLE";
        public const string HealthCheckFailed = "DRIVER.HEALTH_CHECK_FAILED";
    }

    public static class Container
    {
        public const string General = "CONTAINER.GENERAL";
        public const string NotFound = "CONTAINER.NOT_FOUND";
        public const string CreationFailed = "CONTAINER.CREATION_FAILED";
        public const string StartFailed = "CONTAINER.START_FAILED";
        public const string StopFailed = "CONTAINER.STOP_FAILED";
        public const string InvalidState = "CONTAINER.INVALID_STATE";
    }

    public static class Image
    {
        public const string General = "IMAGE.GENERAL";
        public const string NotFound = "IMAGE.NOT_FOUND";
        public const string PullFailed = "IMAGE.PULL_FAILED";
        public const string BuildFailed = "IMAGE.BUILD_FAILED";
    }

    // ... Network, Volume, Compose, Validation
}
```

### Using Error Codes Programmatically

```csharp
try
{
    // Some operation
}
catch (FluentDockerException ex)
{
    switch (ex.ErrorCode)
    {
        case ErrorCodes.Container.NotFound:
            RecreateContainer();
            break;

        case ErrorCodes.Image.NotFound:
            PullImage();
            break;

        case ErrorCodes.Driver.NotAvailable:
            UseFallbackDriver();
            break;

        default:
            Logger.LogError($"Unexpected: {ex.ErrorCode}", ex);
            throw;
    }
}
```

---

## Error Context

The `ErrorContext` class provides rich diagnostic information:

```csharp
public class ErrorContext
{
    // Unique operation identifier for correlation
    public string OperationId { get; set; }

    // Driver where error occurred
    public string DriverId { get; set; }

    // Docker host where error occurred
    public string Host { get; set; }

    // Operation being performed
    public string Operation { get; set; }

    // Resource ID (container, image, network, volume)
    public string ResourceId { get; set; }

    // Resource type
    public string ResourceType { get; set; }

    // Exit code from process execution
    public int? ExitCode { get; set; }

    // Standard output from command
    public string StdOut { get; set; }

    // Standard error from command
    public string StdErr { get; set; }

    // Command that was executed
    public string Command { get; set; }

    // When error occurred
    public DateTime Timestamp { get; set; }

    // Custom properties
    public IDictionary<string, object> Properties { get; set; }
}
```

### Extracting Diagnostic Information

```csharp
catch (FluentDockerException ex)
{
    var ctx = ex.Context;

    Console.WriteLine($"Operation ID: {ctx.OperationId}");
    Console.WriteLine($"Operation: {ctx.Operation}");
    Console.WriteLine($"Timestamp: {ctx.Timestamp}");

    if (!string.IsNullOrEmpty(ctx.DriverId))
    {
        Console.WriteLine($"Driver: {ctx.DriverId}");
        Console.WriteLine($"Host: {ctx.Host}");
    }

    if (ctx.ExitCode.HasValue)
    {
        Console.WriteLine($"Exit code: {ctx.ExitCode}");
        Console.WriteLine($"Command: {ctx.Command}");
        Console.WriteLine($"StdErr: {ctx.StdErr}");
    }
}
```

---

## Migration Examples

### Basic Container Operations

**v2.x.x:**
```csharp
try
{
    using var container = new Builder()
        .UseContainer()
        .UseImage("nginx")
        .Build()
        .Start();
}
catch (FluentDockerException ex)
{
    // Can't determine specific error type
    Console.WriteLine($"Error: {ex.Message}");
}
```

**v3.0.0 (Minimal Changes):**
```csharp
try
{
    await using var container = await new Builder()
        .WithinDriver("docker", kernel)
        .UseContainer(c => c.UseImage("nginx"))
        .BuildAsync();

    await container.All[0].StartAsync();
}
catch (FluentDockerException ex)
{
    // Still works! Plus new features:
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Error Code: {ex.ErrorCode}");
    Console.WriteLine($"Context: {ex.Context}");
}
```

**v3.0.0 (Specific Exception Handling):**
```csharp
try
{
    await using var results = await new Builder()
        .WithinDriver("docker", kernel)
        .UseContainer(c => c.UseImage("nginx"))
        .BuildAsync();

    await results.All[0].StartAsync();
}
catch (ImageNotFoundException ex)
{
    Console.WriteLine($"Image '{ex.ImageName}' not found. Pulling...");
}
catch (ContainerCreationException ex)
{
    Console.WriteLine($"Failed to create: {ex.Message}");
}
catch (ContainerStartException ex)
{
    if (ex.IsTransient)
    {
        Console.WriteLine("Transient error - retrying...");
    }
    else
    {
        throw;
    }
}
```

### Docker Compose

**v2.x.x:**
```csharp
try
{
    var svc = new Builder()
        .UseContainer()
        .UseCompose()
        .FromFile(composeFile)
        .Build()
        .Start();
}
catch (FluentDockerException ex)
{
    // Can't tell if file not found vs validation vs runtime error
    Console.WriteLine($"Compose error: {ex.Message}");
}
```

**v3.0.0:**
```csharp
try
{
    await using var svc = await new Builder()
        .WithinDriver("docker", kernel)
        .UseCompose(c => c.WithComposeFile(composeFile))
        .BuildAsync();
}
catch (ComposeFileNotFoundException ex)
{
    Console.WriteLine($"File not found: {ex.ComposeFile}");
}
catch (ComposeValidationException ex)
{
    Console.WriteLine("Validation errors:");
    foreach (var error in ex.ValidationErrors)
    {
        Console.WriteLine($"  - {error}");
    }
}
catch (ComposeUpException ex)
{
    Console.WriteLine($"Failed: {ex.Message}");
    Console.WriteLine($"Command: {ex.Context?.Command}");
}
```

### Validation Errors

**v3.0.0:**
```csharp
try
{
    await using var container = await new Builder()
        .WithinDriver("docker", kernel)
        .UseContainer(c => c
            // Missing image - will throw ValidationException
            .WithName("test"))
        .BuildAsync();
}
catch (ValidationException ex)
{
    Console.WriteLine("Validation failed:");
    foreach (var error in ex.Errors)
    {
        Console.WriteLine($"  - {error.Property}: {error.Message}");
    }
}
```

---

## Retry Support

### Transient Error Detection

Many exceptions have `IsTransient = true` indicating they can be retried:

- `ImagePullException` - Network issues
- `ContainerStartException` - Resource contention
- `DriverNotAvailableException` - Daemon restarting

```csharp
catch (ImagePullException ex) when (ex.IsTransient)
{
    Console.WriteLine("Transient error - will retry");
    await Task.Delay(TimeSpan.FromSeconds(5));
    // Retry...
}
```

### Built-in Retry Policy

```csharp
var context = new DriverContext
{
    RetryPolicy = new RetryPolicy
    {
        MaxAttempts = 5,
        InitialDelay = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromSeconds(30),
        BackoffMultiplier = 2.0,
        ShouldRetry = ex => ex is FluentDockerException fde && fde.IsTransient
    }
};

// Operations will auto-retry on transient errors
var response = await driver.PullAsync("nginx", context: context);
```

### Manual Retry with RetryExecutor

```csharp
var policy = new RetryPolicy
{
    MaxAttempts = 3,
    InitialDelay = TimeSpan.FromSeconds(1),
    BackoffMultiplier = 2.0
};

var result = await RetryExecutor.ExecuteAsync(async () =>
{
    var response = await kernel.PullImageAsync("nginx");

    if (!response.Success)
    {
        throw new ImagePullException("nginx", response.Error)
        {
            IsTransient = response.IsTransientError
        };
    }

    return response.Data;
}, policy);
```

---

## Exception Type Mapping

| v2.x.x | v3.0.0 | When to Use |
|--------|--------|-------------|
| `FluentDockerException` | `FluentDockerException` | General errors (still works) |
| `FluentDockerException` | `ContainerNotFoundException` | Container doesn't exist |
| `FluentDockerException` | `ContainerStartException` | Start failed |
| `FluentDockerException` | `ImageNotFoundException` | Image doesn't exist |
| `FluentDockerException` | `ImagePullException` | Pull failed |
| `FluentDockerException` | `DriverNotFoundException` | Driver not registered |
| `FluentDockerException` | `ComposeException` | Compose operation failed |
| `FluentDockerNotSupportedException` | `FluentDockerNotSupportedException` | Unchanged |

---

## Best Practices

### 1. Catch Specific Exceptions First

```csharp
try
{
    await container.StartAsync();
}
catch (ContainerStartException ex) when (ex.IsTransient)
{
    // Handle transient start failure
}
catch (ContainerStartException ex)
{
    // Handle permanent start failure
}
catch (ContainerNotFoundException ex)
{
    // Container was removed
}
catch (FluentDockerException ex)
{
    // Catch-all for other errors
}
```

### 2. Use Error Codes for Programmatic Decisions

```csharp
catch (FluentDockerException ex)
{
    switch (ex.ErrorCode)
    {
        case ErrorCodes.Container.NotFound:
        case ErrorCodes.Container.InvalidState:
            // Recreate container
            break;
        default:
            throw;
    }
}
```

### 3. Log Error Context

```csharp
catch (FluentDockerException ex)
{
    logger.LogError(ex,
        "Operation {Operation} failed for {ResourceType}/{ResourceId}. " +
        "Driver: {DriverId}, Host: {Host}, ExitCode: {ExitCode}",
        ex.Context?.Operation,
        ex.Context?.ResourceType,
        ex.Context?.ResourceId,
        ex.Context?.DriverId,
        ex.Context?.Host,
        ex.Context?.ExitCode);
}
```

### 4. Handle Transient Errors with Retry

```csharp
catch (FluentDockerException ex) when (ex.IsTransient)
{
    // Use built-in retry or implement backoff
    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    // Retry operation
}
```

---

## Summary

FluentDocker v3.0 error handling provides:

- **20+ typed exceptions** for precise error handling
- **Hierarchical error codes** for programmatic decisions
- **Rich diagnostic context** (OperationId, DriverId, ExitCode, StdErr)
- **Transient error detection** with IsTransient flag
- **Built-in retry mechanisms** with configurable policies
- **Backward compatibility** - existing catch blocks still work
