# Error Handling Migration Guide - v2.x.x to v3.0.0

## Overview

This guide provides step-by-step migration instructions for error handling changes between FluentDocker v2.x.x and v3.0.0.

**Key Changes:**
- New typed exception hierarchy (20+ exception types)
- Error codes for programmatic identification
- Enhanced CommandResponse with error context
- Retry mechanisms for transient errors
- Structured logging and metrics

**Migration Approach**: Incremental - existing catch blocks continue to work, new features are opt-in.

---

## Quick Migration Reference

### Exception Type Mapping

| v2.x.x Exception | v3.0.0 Equivalent | When to Use |
|------------------|-------------------|-------------|
| `FluentDockerException` | `FluentDockerException` (base) | General errors (still works) |
| `FluentDockerException` | `ContainerNotFoundException` | Container doesn't exist |
| `FluentDockerException` | `ContainerStartException` | Container start failed |
| `FluentDockerException` | `ContainerStopException` | Container stop failed |
| `FluentDockerException` | `ContainerCreationException` | Container creation failed |
| `FluentDockerException` | `ImageNotFoundException` | Image doesn't exist |
| `FluentDockerException` | `ImagePullException` | Image pull failed |
| `FluentDockerException` | `ImageBuildException` | Image build failed |
| `FluentDockerException` | `NetworkNotFoundException` | Network doesn't exist |
| `FluentDockerException` | `VolumeNotFoundException` | Volume doesn't exist |
| `FluentDockerException` | `DriverNotFoundException` | Driver not registered |
| `FluentDockerException` | `DriverNotAvailableException` | Driver daemon down/binary missing |
| `FluentDockerException` | `ComposeException` | Compose operation failed |
| `FluentDockerException` | `ValidationException` | Configuration validation failed |
| `FluentDockerNotSupportedException` | `FluentDockerNotSupportedException` | Unchanged |

---

## Migration Scenarios

### Scenario 1: Basic Container Operations

**v2.x.x Code:**

```csharp
using FluentDocker.Builders;
using FluentDocker.Common;

public void StartContainer()
{
    try
    {
        using var container = new Builder()
            .UseContainer()
            .UseImage("nginx")
            .Build()
            .Start();

        // Use container
    }
    catch (FluentDockerException ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        // Can't determine specific error type
    }
}
```

**v3.0.0 Code (Option 1 - Minimal Changes):**

```csharp
using FluentDocker.Builders;
using FluentDocker.Exceptions;

public void StartContainer()
{
    try
    {
        using var container = new Builder()
            .UseContainer()
            .UseImage("nginx")
            .Build()
            .Start();

        // Use container
    }
    catch (FluentDockerException ex)
    {
        // Still works! Base exception catches all
        Console.WriteLine($"Error: {ex.Message}");

        // NEW: Access error code
        Console.WriteLine($"Error Code: {ex.ErrorCode}");

        // NEW: Access context
        if (ex.Context != null)
        {
            Console.WriteLine($"Operation: {ex.Context.Operation}");
            Console.WriteLine($"Driver: {ex.Context.DriverId}");
        }
    }
}
```

**v3.0.0 Code (Option 2 - Specific Exception Handling):**

```csharp
using FluentDocker.Builders;
using FluentDocker.Exceptions;

public void StartContainer()
{
    try
    {
        using var container = new Builder()
            .UseContainer()
            .UseImage("nginx")
            .Build()
            .Start();

        // Use container
    }
    catch (ImageNotFoundException ex)
    {
        Console.WriteLine($"Image '{ex.ImageName}' not found. Pulling...");
        // Try to pull image first
    }
    catch (ContainerCreationException ex)
    {
        Console.WriteLine($"Failed to create container: {ex.Message}");
        Console.WriteLine($"Create params: {ex.CreateParams?.Image}");
        // Log detailed error
    }
    catch (ContainerStartException ex)
    {
        if (ex.IsTransient)
        {
            Console.WriteLine("Transient error - retrying...");
            // Retry logic
        }
        else
        {
            Console.WriteLine($"Permanent start failure: {ex.Message}");
            throw;
        }
    }
    catch (FluentDockerException ex)
    {
        // Catch-all for unexpected errors
        Console.WriteLine($"Unexpected error [{ex.ErrorCode}]: {ex.Message}");
        throw;
    }
}
```

### Scenario 2: Image Pull Operations

**v2.x.x Code:**

```csharp
public void PullImage(string imageName)
{
    var host = new DockerUri("unix:///var/run/docker.sock");

    try
    {
        var response = host.Pull(imageName);

        if (!response.Success)
        {
            // Must manually check success
            Console.WriteLine($"Pull failed: {response.Error}");

            // No way to know if error is transient
            // Can't easily retry
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Pull error: {ex.Message}");
    }
}
```

**v3.0.0 Code:**

```csharp
using FluentDocker.Exceptions;
using FluentDocker.Resilience;

public async Task PullImageAsync(string imageName)
{
    var kernel = new FluentDockerKernel();

    // Option 1: Manual retry
    try
    {
        var context = new DriverContext
        {
            RetryPolicy = RetryPolicy.Default  // Auto-retry transient errors
        };

        var response = kernel.PullImage(imageName, context);
        response.ThrowIfFailed();  // Convert to exception if failed

        Console.WriteLine($"Successfully pulled {imageName}");
    }
    catch (ImagePullException ex) when (ex.IsTransient)
    {
        // Transient error (network issue, registry timeout)
        Console.WriteLine($"Transient pull error, will retry: {ex.Message}");
        await Task.Delay(TimeSpan.FromSeconds(5));
        // Retry...
    }
    catch (ImagePullException ex)
    {
        // Permanent error (image doesn't exist, auth failed)
        Console.WriteLine($"Permanent pull error: {ex.Message}");
        Console.WriteLine($"Error code: {ex.ErrorCode}");
        throw;
    }

    // Option 2: Built-in retry (v3.0.0 feature)
    var context2 = new DriverContext
    {
        RetryPolicy = new RetryPolicy
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromSeconds(2),
            BackoffMultiplier = 2.0,
            ShouldRetry = ex => ex is ImagePullException ipe && ipe.IsTransient
        }
    };

    var response2 = await kernel.PullImageAsync(imageName, context2);
    // Auto-retries up to 5 times with exponential backoff
}
```

### Scenario 3: Container Start/Stop

**v2.x.x Code:**

```csharp
public void ManageContainer(IContainerService container)
{
    try
    {
        container.Start();
        Console.WriteLine("Container started");

        // Do work...

        container.Stop();
        Console.WriteLine("Container stopped");
    }
    catch (FluentDockerException ex)
    {
        // Can't tell if it's start or stop that failed
        Console.WriteLine($"Container operation failed: {ex.Message}");
    }
}
```

**v3.0.0 Code:**

```csharp
using FluentDocker.Exceptions;

public void ManageContainer(IContainerService container)
{
    try
    {
        container.Start();
        Console.WriteLine($"Container {container.Id} started");

        // Do work...

        container.Stop();
        Console.WriteLine($"Container {container.Id} stopped");
    }
    catch (ContainerStartException ex)
    {
        Console.WriteLine($"Failed to start container {ex.ContainerId}");
        Console.WriteLine($"Reason: {ex.Message}");
        Console.WriteLine($"Error code: {ex.ErrorCode}");

        // Check context for details
        if (ex.Context?.ExitCode != null)
        {
            Console.WriteLine($"Exit code: {ex.Context.ExitCode}");
            Console.WriteLine($"StdErr: {ex.Context.StdErr}");
        }
    }
    catch (ContainerStopException ex)
    {
        Console.WriteLine($"Failed to stop container {ex.ContainerId}");

        if (ex.IsTransient)
        {
            // Daemon might be restarting
            Console.WriteLine("Transient error - daemon may be restarting");
        }
    }
    catch (InvalidContainerStateException ex)
    {
        Console.WriteLine($"Container {ex.ContainerId} is in invalid state");
        Console.WriteLine($"Current: {ex.CurrentState}, Expected: {ex.ExpectedState}");

        // Can programmatically handle based on state
        if (ex.CurrentState == "stopped")
        {
            // Already stopped, that's fine
        }
    }
}
```

### Scenario 4: Docker Compose

**v2.x.x Code:**

```csharp
public void RunCompose(string composeFile)
{
    try
    {
        var svc = new Builder()
            .UseContainer()
            .UseCompose()
            .FromFile(composeFile)
            .Build()
            .Start();

        Console.WriteLine($"Compose started with {svc.Containers.Count} containers");
    }
    catch (FluentDockerException ex)
    {
        // Can't tell if file not found vs validation error vs runtime error
        Console.WriteLine($"Compose error: {ex.Message}");
    }
}
```

**v3.0.0 Code:**

```csharp
using FluentDocker.Exceptions;

public void RunCompose(string composeFile)
{
    try
    {
        var svc = new Builder()
            .UseContainer()
            .UseCompose()
            .FromFile(composeFile)
            .Build()
            .Start();

        Console.WriteLine($"Compose started with {svc.Containers.Count} containers");
    }
    catch (ComposeFileNotFoundException ex)
    {
        Console.WriteLine($"Compose file not found: {ex.ComposeFile}");
        // Check if file exists, print correct path
    }
    catch (ComposeValidationException ex)
    {
        Console.WriteLine($"Compose file validation failed:");
        foreach (var error in ex.ValidationErrors)
        {
            Console.WriteLine($"  - {error}");
        }
        // Can show user exactly what's wrong with their compose file
    }
    catch (ComposeUpException ex)
    {
        Console.WriteLine($"Failed to bring up compose project '{ex.ProjectName}'");
        Console.WriteLine($"Error: {ex.Message}");

        // Check context for details
        if (ex.Context != null)
        {
            Console.WriteLine($"Operation: {ex.Context.Operation}");
            Console.WriteLine($"Command: {ex.Context.Command}");
        }
    }
    catch (ComposeException ex)
    {
        // Catch-all for other compose errors
        Console.WriteLine($"Compose error [{ex.ErrorCode}]: {ex.Message}");
    }
}
```

### Scenario 5: Driver Management

**v2.x.x Code:**

N/A - Drivers don't exist in v2.x.x

**v3.0.0 Code:**

```csharp
using FluentDocker.Drivers.Exceptions;
using FluentDocker.Exceptions;

public void ManageDrivers()
{
    var kernel = new FluentDockerKernel(autoRegister: false);

    // Register drivers with error handling
    try
    {
        kernel.RegisterDriver("docker-cli", new DockerCliDriver());
        Console.WriteLine("Docker CLI driver registered");
    }
    catch (DriverRegistrationException ex)
    {
        Console.WriteLine($"Failed to register driver: {ex.Message}");
        // Driver ID already exists
    }

    // Use specific driver
    try
    {
        var driver = kernel.GetDriver("docker-cli");
        var healthStatus = driver.HealthCheck(new DriverContext());

        if (!healthStatus.IsHealthy)
        {
            Console.WriteLine($"Driver unhealthy: {healthStatus.Message}");
        }
    }
    catch (DriverNotFoundException ex)
    {
        Console.WriteLine($"Driver '{ex.DriverId}' not found");
        // List available drivers
        var available = kernel.GetDriverIds();
        Console.WriteLine($"Available drivers: {string.Join(", ", available)}");
    }
    catch (DriverNotAvailableException ex)
    {
        Console.WriteLine($"Driver '{ex.DriverId}' not available: {ex.Reason}");
        // Docker daemon down or binary not found
        // Can show user how to fix (install Docker, start daemon)
    }

    // Driver selection
    try
    {
        var context = new DriverContext
        {
            Preferences = new DriverPreferences
            {
                PreferredDriverIds = new List<string> { "docker-api", "docker-cli" },
                AllowFallback = true
            }
        };

        var container = kernel.CreateContainer(createParams, context);
    }
    catch (DriverSelectionException ex)
    {
        Console.WriteLine($"Could not select driver: {ex.Message}");

        if (ex.Criteria != null)
        {
            Console.WriteLine($"Criteria: Runtime={ex.Criteria.RequiredRuntime}");
        }

        // List what drivers are available
        Console.WriteLine("Available drivers:");
        foreach (var driverId in kernel.GetDriverIds())
        {
            var driver = kernel.GetDriver(driverId);
            Console.WriteLine($"  - {driverId} ({driver.Runtime}, {driver.Type})");
        }
    }
}
```

### Scenario 6: Validation Errors

**v2.x.x Code:**

```csharp
public void BuildContainer()
{
    try
    {
        var container = new Builder()
            .UseContainer()
            // Missing image - will throw generic exception
            .Build();
    }
    catch (FluentDockerException ex)
    {
        // Can't tell what validation failed
        Console.WriteLine($"Build error: {ex.Message}");
    }
}
```

**v3.0.0 Code:**

```csharp
using FluentDocker.Exceptions;

public void BuildContainer()
{
    try
    {
        var container = new Builder()
            .UseContainer()
            // Missing image - will throw ValidationException
            .Build();
    }
    catch (ValidationException ex)
    {
        Console.WriteLine($"Configuration validation failed:");

        // Show all validation errors
        foreach (var error in ex.Errors)
        {
            Console.WriteLine($"  - {error.Property}: {error.Message}");
            if (error.AttemptedValue != null)
            {
                Console.WriteLine($"    Attempted value: {error.AttemptedValue}");
            }
        }

        // Can programmatically fix validation errors
        if (ex.Errors.Any(e => e.Property == "Image"))
        {
            Console.WriteLine("Please specify an image using .UseImage(\"image-name\")");
        }
    }
    catch (ContainerCreationException ex)
    {
        // Runtime error (after validation passed)
        Console.WriteLine($"Container creation failed: {ex.Message}");
    }
}
```

---

## Error Code Usage

### Programmatic Error Handling

**v3.0.0 Pattern:**

```csharp
using FluentDocker.Exceptions;

public void HandleErrors()
{
    try
    {
        // Some operation
    }
    catch (FluentDockerException ex)
    {
        // Use error codes for programmatic decisions
        switch (ex.ErrorCode)
        {
            case ErrorCodes.Container.NotFound:
                // Recreate container
                RecreateContainer();
                break;

            case ErrorCodes.Container.InvalidState:
                // Reset container state
                ResetContainer();
                break;

            case ErrorCodes.Image.NotFound:
                // Pull image first
                PullImage();
                break;

            case ErrorCodes.Driver.NotAvailable:
                // Try different driver
                UseFallbackDriver();
                break;

            case ErrorCodes.Network.NotFound:
                // Create network
                CreateNetwork();
                break;

            default:
                // Log and rethrow
                Logger.LogError($"Unexpected error: {ex.ErrorCode}", ex);
                throw;
        }
    }
}
```

### Error Code Constants

```csharp
// All error codes are in ErrorCodes class
using static FluentDocker.Exceptions.ErrorCodes;

try
{
    // Operation
}
catch (FluentDockerException ex) when (ex.ErrorCode == Container.NotFound)
{
    // Handle container not found
}
catch (FluentDockerException ex) when (ex.ErrorCode == Image.PullFailed)
{
    // Handle image pull failure
}
```

---

## Using Error Context

### Extract Diagnostic Information

```csharp
using FluentDocker.Exceptions;

public void DiagnoseError(FluentDockerException ex)
{
    var ctx = ex.Context;

    // Operation tracking
    Console.WriteLine($"Operation ID: {ctx.OperationId}");
    Console.WriteLine($"Operation: {ctx.Operation}");
    Console.WriteLine($"Timestamp: {ctx.Timestamp}");

    // Driver information
    if (!string.IsNullOrEmpty(ctx.DriverId))
    {
        Console.WriteLine($"Driver: {ctx.DriverId}");
        Console.WriteLine($"Host: {ctx.Host}");
    }

    // Resource information
    if (!string.IsNullOrEmpty(ctx.ResourceType))
    {
        Console.WriteLine($"Resource: {ctx.ResourceType}/{ctx.ResourceId}");
    }

    // Process execution details
    if (ctx.ExitCode.HasValue)
    {
        Console.WriteLine($"Exit code: {ctx.ExitCode}");
        Console.WriteLine($"Command: {ctx.Command}");

        if (!string.IsNullOrEmpty(ctx.StdOut))
            Console.WriteLine($"StdOut: {ctx.StdOut}");

        if (!string.IsNullOrEmpty(ctx.StdErr))
            Console.WriteLine($"StdErr: {ctx.StdErr}");
    }

    // Custom properties
    foreach (var prop in ctx.Properties)
    {
        Console.WriteLine($"{prop.Key}: {prop.Value}");
    }
}
```

### Correlation Across Operations

```csharp
public async Task PerformComplexOperation()
{
    var operationId = Guid.NewGuid().ToString("N");

    try
    {
        // Pull image
        var pullContext = new DriverContext();
        pullContext.Metadata["OperationId"] = operationId;
        await kernel.PullImageAsync("nginx", pullContext);

        // Create container
        var createContext = new DriverContext();
        createContext.Metadata["OperationId"] = operationId;
        var container = kernel.CreateContainer(createParams, createContext);

        // Start container
        var startContext = new DriverContext();
        startContext.Metadata["OperationId"] = operationId;
        kernel.StartContainer(container.Data, startContext);

        Console.WriteLine($"Complex operation {operationId} completed successfully");
    }
    catch (FluentDockerException ex)
    {
        // All errors will have same operation ID
        Console.WriteLine($"Complex operation {operationId} failed");
        Console.WriteLine($"Error at step: {ex.Context?.Operation}");
        Console.WriteLine($"Error: {ex.Message}");

        // Can trace entire operation in logs
    }
}
```

---

## Retry Patterns

### Built-in Retry Support

**v3.0.0 Feature:**

```csharp
using FluentDocker.Resilience;

// Method 1: Use RetryPolicy in DriverContext
public async Task PullWithRetry(string image)
{
    var context = new DriverContext
    {
        RetryPolicy = RetryPolicy.Default  // 3 attempts, exponential backoff
    };

    var response = await kernel.PullImageAsync(image, context);
    // Automatically retries on transient errors
}

// Method 2: Custom RetryPolicy
public async Task PullWithCustomRetry(string image)
{
    var context = new DriverContext
    {
        RetryPolicy = new RetryPolicy
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromSeconds(2),
            MaxDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2.0,
            ShouldRetry = ex =>
            {
                // Custom retry logic
                if (ex is ImagePullException ipe)
                {
                    // Retry network errors
                    if (ipe.Message.Contains("network") || ipe.Message.Contains("timeout"))
                        return true;

                    // Don't retry auth errors
                    if (ipe.Message.Contains("unauthorized") || ipe.Message.Contains("forbidden"))
                        return false;
                }

                // Retry all transient errors
                return ex is FluentDockerException fde && fde.IsTransient;
            }
        }
    };

    var response = await kernel.PullImageAsync(image, context);
}

// Method 3: Manual retry with RetryExecutor
public async Task ManualRetry()
{
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
                IsTransient = DetermineIfTransient(response)
            };
        }

        return response.Data;
    }, policy);
}
```

---

## Logging Integration

### v2.x.x Logging

```csharp
// v2.x.x has minimal logging
// Only debug traces via Logger.Log()

Logger.Log("Some debug message");
// Output: [FluentDocker] Some debug message
```

### v3.0.0 Logging

```csharp
using FluentDocker.Logging;

// Setup custom logger (once at app startup)
public void ConfigureLogging()
{
    // Option 1: Use built-in logger with levels
    var logger = LoggerFactory.GetLogger();
    logger.LogInformation("FluentDocker initialized");
    logger.LogWarning("Docker daemon might be slow");
    logger.LogError("Container creation failed", exception: ex);

    // Option 2: Integrate with your logging framework
    LoggerFactory.SetLogger(new SerilogAdapter());  // Custom adapter
}

// Custom logger adapter
public class SerilogAdapter : IFluentDockerLogger
{
    private readonly ILogger _logger = Log.ForContext<FluentDockerKernel>();

    public void Log(LogLevel level, string message, ErrorContext context = null, Exception exception = null)
    {
        var logContext = new
        {
            context?.OperationId,
            context?.DriverId,
            context?.Operation,
            context?.ResourceType,
            context?.ResourceId
        };

        switch (level)
        {
            case LogLevel.Trace:
                _logger.Verbose(exception, message, logContext);
                break;
            case LogLevel.Debug:
                _logger.Debug(exception, message, logContext);
                break;
            case LogLevel.Information:
                _logger.Information(exception, message, logContext);
                break;
            case LogLevel.Warning:
                _logger.Warning(exception, message, logContext);
                break;
            case LogLevel.Error:
                _logger.Error(exception, message, logContext);
                break;
            case LogLevel.Critical:
                _logger.Fatal(exception, message, logContext);
                break;
        }
    }

    // Implement other interface methods...
}

// Use in code
public void PerformOperation()
{
    var logger = LoggerFactory.GetLogger();

    try
    {
        logger.LogInformation("Starting container operation");
        container.Start();
        logger.LogInformation("Container started successfully");
    }
    catch (ContainerStartException ex)
    {
        logger.LogError("Container start failed", ex.Context, ex);
        throw;
    }
}
```

---

## Testing Error Handling

### Unit Tests - v2.x.x

```csharp
[Fact]
public void Should_Throw_Exception_When_Container_Not_Found()
{
    // v2.x.x - can only check generic exception
    var ex = Assert.Throws<FluentDockerException>(() =>
    {
        service.Start();
    });

    // Must parse message string
    Assert.Contains("not found", ex.Message.ToLower());
}
```

### Unit Tests - v3.0.0

```csharp
using FluentDocker.Exceptions;

[Fact]
public void Should_Throw_ContainerNotFoundException_When_Container_Not_Found()
{
    // v3.0.0 - can check specific exception type
    var ex = Assert.Throws<ContainerNotFoundException>(() =>
    {
        service.Start();
    });

    Assert.Equal("container-id-123", ex.ContainerId);
    Assert.Equal(ErrorCodes.Container.NotFound, ex.ErrorCode);
}

[Fact]
public void Should_Include_Error_Context_In_Exception()
{
    var ex = Assert.Throws<ContainerStartException>(() =>
    {
        service.Start();
    });

    Assert.NotNull(ex.Context);
    Assert.Equal("Container.Start", ex.Context.Operation);
    Assert.Equal("docker-cli", ex.Context.DriverId);
    Assert.NotNull(ex.Context.OperationId);
}

[Fact]
public void Should_Mark_Network_Errors_As_Transient()
{
    var ex = Assert.Throws<ImagePullException>(() =>
    {
        driver.PullImage(context, "nginx");
    });

    // Network errors should be marked transient
    Assert.True(ex.IsTransient);
}

[Fact]
public void Should_Retry_Transient_Errors()
{
    var attempts = 0;
    var policy = new RetryPolicy
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromMilliseconds(10),
        ShouldRetry = ex => ex is ImagePullException ipe && ipe.IsTransient
    };

    Assert.Throws<ImagePullException>(() =>
    {
        RetryExecutor.Execute(() =>
        {
            attempts++;
            throw new ImagePullException("nginx", "network error") { IsTransient = true };
        }, policy);
    });

    // Should have tried 3 times
    Assert.Equal(3, attempts);
}
```

---

## Migration Checklist

### For Library Maintainers

- [ ] Phase 1: Add new exception types (Week 1)
  - [ ] Create `Exceptions/` namespace
  - [ ] Implement exception hierarchy
  - [ ] Create `ErrorCodes` class
  - [ ] Create `ErrorContext` class

- [ ] Phase 2: Enhance CommandResponse (Week 1)
  - [ ] Add optional error context parameter
  - [ ] Add optional error code parameter
  - [ ] Add `ThrowIfFailed()` method
  - [ ] Add `CreateException()` method

- [ ] Phase 3: Update drivers (Weeks 2-3)
  - [ ] Drivers return enriched CommandResponse
  - [ ] Set error codes in responses
  - [ ] Populate error context
  - [ ] Mark transient errors

- [ ] Phase 4: Update services (Week 4)
  - [ ] Services throw typed exceptions
  - [ ] Include error context
  - [ ] Set IsTransient flag

- [ ] Phase 5: Update builders (Week 4)
  - [ ] Add validation framework
  - [ ] Throw ValidationException
  - [ ] Improve error messages

- [ ] Phase 6: Add retry support (Week 5)
  - [ ] Implement RetryPolicy
  - [ ] Implement RetryExecutor
  - [ ] Add RetryPolicy to DriverContext

- [ ] Phase 7: Add logging (Week 5)
  - [ ] Implement IFluentDockerLogger
  - [ ] Add LoggerFactory
  - [ ] Add structured logging throughout

- [ ] Phase 8: Testing (Week 6)
  - [ ] Unit tests for all exception types
  - [ ] Integration tests with error scenarios
  - [ ] Retry mechanism tests

- [ ] Phase 9: Documentation (Week 6)
  - [ ] Update API documentation
  - [ ] Create migration guide
  - [ ] Add code examples

### For Library Users

- [ ] Review breaking changes document
- [ ] Identify existing catch blocks
- [ ] Decide migration approach:
  - [ ] Option A: Keep catching `FluentDockerException` (minimal changes)
  - [ ] Option B: Update to specific exception types (recommended)
  - [ ] Option C: Use error codes for decisions

- [ ] Update exception handling:
  - [ ] Replace generic catch blocks with specific types
  - [ ] Add error code handling where needed
  - [ ] Use error context for diagnostics

- [ ] Add retry logic where appropriate:
  - [ ] Image pull operations
  - [ ] Network operations
  - [ ] Daemon communication

- [ ] Integrate with logging:
  - [ ] Configure LoggerFactory
  - [ ] Add structured logging

- [ ] Update tests:
  - [ ] Change to specific exception assertions
  - [ ] Test error context population
  - [ ] Test retry behavior

---

## Summary

### Migration Effort by User Type

| User Type | Effort | Recommended Approach |
|-----------|--------|---------------------|
| Basic usage (simple containers) | **Low** | Keep catching `FluentDockerException`, optionally add error codes |
| Production usage | **Medium** | Migrate to specific exceptions, add retry logic |
| Library integration | **Medium-High** | Full migration with logging integration |
| Advanced scenarios (multi-host, compose) | **High** | Full migration with all features |

### Benefits Summary

✅ **Better error identification** - Know exactly what failed
✅ **Programmatic error handling** - Use error codes for decisions
✅ **Rich diagnostics** - Error context for troubleshooting
✅ **Transient error handling** - Built-in retry support
✅ **Better observability** - Structured logging and metrics
✅ **Easier testing** - Mock specific exceptions
✅ **Backward compatible** - Existing code continues to work

The migration path is designed to be **incremental** - you can adopt new features gradually without breaking existing code.
