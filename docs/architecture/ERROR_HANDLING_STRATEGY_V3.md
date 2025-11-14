# FluentDocker v3.0.0 - Error Handling and Exception Strategy

## Executive Summary

This document defines a comprehensive error handling strategy for FluentDocker v3.0.0, addressing gaps in the current v2.x.x implementation and introducing consistent patterns across the driver layer architecture.

**Current State (v2.x.x):**
- 2 custom exceptions (`FluentDockerException`, `FluentDockerNotSupportedException`)
- Mixed error handling (exceptions in Services/Builders, `CommandResponse<T>` in Commands)
- No error codes or structured error information
- Minimal logging (debug traces only)
- No diagnostic context or correlation

**v3.0.0 Goals:**
- Typed exception hierarchy for driver errors
- Consistent error handling across all layers
- Structured error information with context
- Better observability (logging, metrics, diagnostics)
- Error recovery and retry mechanisms
- Clear migration path from v2.x.x

---

## Current Error Handling Analysis

### Exception Types (v2.x.x)

**File**: `Common/FluentDockerException.cs`
```csharp
public class FluentDockerException : Exception
{
    public FluentDockerException()
    public FluentDockerException(string message) : base(message)
    public FluentDockerException(string message, Exception innerException) : base(message, innerException)
}
```

**File**: `Services/FluentDockerNotSupportedException.cs`
```csharp
public class FluentDockerNotSupportedException : FluentDockerException
{
    // Indicates unsupported feature/operation
}
```

### Error Patterns (v2.x.x)

| Layer | Pattern | Example |
|-------|---------|---------|
| Commands | Return `CommandResponse<T>` | `if (!response.Success) return errorResponse;` |
| Services | Throw exceptions | `throw new FluentDockerException($"Failed to start: {result}")` |
| Builders | Throw exceptions | `throw new FluentDockerException("Missing required config")` |
| ProcessExecutor | Throw + Response | Throws on process start failure, returns response otherwise |

### Identified Gaps

1. **No typed exceptions** - Can't catch specific errors (container not found, image pull failed, etc.)
2. **No error codes** - String parsing required to identify error types
3. **Inconsistent patterns** - Some methods throw, others return errors
4. **Poor diagnostics** - No context (operation ID, driver ID, host info)
5. **No observability** - Minimal logging, no metrics
6. **No recovery** - No retry mechanisms or fallback strategies
7. **Mixed concerns** - Error handling logic scattered

---

## v3.0.0 Exception Hierarchy

### Base Exceptions

```csharp
namespace Ductus.FluentDocker.Exceptions
{
    /// <summary>
    /// Base exception for all FluentDocker errors.
    /// </summary>
    public class FluentDockerException : Exception
    {
        /// <summary>
        /// Error code for programmatic error identification.
        /// </summary>
        public string ErrorCode { get; set; }

        /// <summary>
        /// Diagnostic context for troubleshooting.
        /// </summary>
        public ErrorContext Context { get; set; }

        /// <summary>
        /// Whether this error is transient and can be retried.
        /// </summary>
        public bool IsTransient { get; set; }

        /// <summary>
        /// HTTP status code equivalent (for API drivers).
        /// </summary>
        public int? StatusCode { get; set; }

        public FluentDockerException(string message) : base(message)
        {
            Context = new ErrorContext();
        }

        public FluentDockerException(string message, Exception innerException)
            : base(message, innerException)
        {
            Context = new ErrorContext();
        }

        public FluentDockerException(string errorCode, string message, ErrorContext context = null)
            : base(message)
        {
            ErrorCode = errorCode;
            Context = context ?? new ErrorContext();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(base.ToString());

            if (!string.IsNullOrEmpty(ErrorCode))
                sb.AppendLine($"Error Code: {ErrorCode}");

            if (Context != null && Context.HasContext)
                sb.AppendLine($"Context: {Context}");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Exception for unsupported features or operations.
    /// </summary>
    public class FluentDockerNotSupportedException : FluentDockerException
    {
        public FluentDockerNotSupportedException(string message) : base(message)
        {
            ErrorCode = ErrorCodes.NotSupported;
        }

        public FluentDockerNotSupportedException(string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = ErrorCodes.NotSupported;
        }
    }
}
```

### Driver Exceptions

```csharp
namespace Ductus.FluentDocker.Drivers.Exceptions
{
    /// <summary>
    /// Base exception for driver-related errors.
    /// </summary>
    public class DriverException : FluentDockerException
    {
        public string DriverId { get; set; }
        public string DriverType { get; set; }

        public DriverException(string message) : base(message)
        {
            ErrorCode = ErrorCodes.Driver.General;
        }

        public DriverException(string message, string driverId)
            : base(message)
        {
            DriverId = driverId;
            ErrorCode = ErrorCodes.Driver.General;
        }

        public DriverException(string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = ErrorCodes.Driver.General;
        }
    }

    /// <summary>
    /// Exception thrown when a driver cannot be found.
    /// </summary>
    public class DriverNotFoundException : DriverException
    {
        public DriverNotFoundException(string driverId)
            : base($"Driver '{driverId}' not found", driverId)
        {
            ErrorCode = ErrorCodes.Driver.NotFound;
        }

        public DriverNotFoundException(string message, string driverId)
            : base(message, driverId)
        {
            ErrorCode = ErrorCodes.Driver.NotFound;
        }
    }

    /// <summary>
    /// Exception thrown when a driver is not available (binary missing, daemon down, etc.).
    /// </summary>
    public class DriverNotAvailableException : DriverException
    {
        public string Reason { get; set; }

        public DriverNotAvailableException(string driverId, string reason)
            : base($"Driver '{driverId}' is not available: {reason}", driverId)
        {
            Reason = reason;
            ErrorCode = ErrorCodes.Driver.NotAvailable;
            IsTransient = true;  // May become available later
        }
    }

    /// <summary>
    /// Exception thrown when a driver health check fails.
    /// </summary>
    public class DriverHealthCheckException : DriverException
    {
        public DriverHealthStatus HealthStatus { get; set; }

        public DriverHealthCheckException(string driverId, DriverHealthStatus healthStatus)
            : base($"Driver '{driverId}' health check failed: {healthStatus.Message}", driverId)
        {
            HealthStatus = healthStatus;
            ErrorCode = ErrorCodes.Driver.HealthCheckFailed;
            IsTransient = true;
        }
    }

    /// <summary>
    /// Exception thrown when driver registration fails.
    /// </summary>
    public class DriverRegistrationException : DriverException
    {
        public DriverRegistrationException(string message)
            : base(message)
        {
            ErrorCode = ErrorCodes.Driver.RegistrationFailed;
        }

        public DriverRegistrationException(string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = ErrorCodes.Driver.RegistrationFailed;
        }
    }

    /// <summary>
    /// Exception thrown when no suitable driver can be selected.
    /// </summary>
    public class DriverSelectionException : DriverException
    {
        public DriverSelectionCriteria Criteria { get; set; }

        public DriverSelectionException(string message, DriverSelectionCriteria criteria = null)
            : base(message)
        {
            Criteria = criteria;
            ErrorCode = ErrorCodes.Driver.SelectionFailed;
        }
    }
}
```

### Container Operation Exceptions

```csharp
namespace Ductus.FluentDocker.Exceptions
{
    /// <summary>
    /// Base exception for container operations.
    /// </summary>
    public class ContainerException : FluentDockerException
    {
        public string ContainerId { get; set; }
        public string ContainerName { get; set; }

        public ContainerException(string message, string containerId = null)
            : base(message)
        {
            ContainerId = containerId;
            ErrorCode = ErrorCodes.Container.General;
        }
    }

    /// <summary>
    /// Exception thrown when a container cannot be found.
    /// </summary>
    public class ContainerNotFoundException : ContainerException
    {
        public ContainerNotFoundException(string containerId)
            : base($"Container '{containerId}' not found", containerId)
        {
            ErrorCode = ErrorCodes.Container.NotFound;
            StatusCode = 404;
        }
    }

    /// <summary>
    /// Exception thrown when container creation fails.
    /// </summary>
    public class ContainerCreationException : ContainerException
    {
        public ContainerCreateParams CreateParams { get; set; }

        public ContainerCreationException(string message, ContainerCreateParams createParams = null)
            : base(message)
        {
            CreateParams = createParams;
            ErrorCode = ErrorCodes.Container.CreationFailed;
        }
    }

    /// <summary>
    /// Exception thrown when starting a container fails.
    /// </summary>
    public class ContainerStartException : ContainerException
    {
        public ContainerStartException(string containerId, string reason)
            : base($"Failed to start container '{containerId}': {reason}", containerId)
        {
            ErrorCode = ErrorCodes.Container.StartFailed;
            IsTransient = true;
        }
    }

    /// <summary>
    /// Exception thrown when stopping a container fails.
    /// </summary>
    public class ContainerStopException : ContainerException
    {
        public ContainerStopException(string containerId, string reason)
            : base($"Failed to stop container '{containerId}': {reason}", containerId)
        {
            ErrorCode = ErrorCodes.Container.StopFailed;
            IsTransient = true;
        }
    }

    /// <summary>
    /// Exception thrown when a container is in an invalid state for the operation.
    /// </summary>
    public class InvalidContainerStateException : ContainerException
    {
        public string CurrentState { get; set; }
        public string ExpectedState { get; set; }

        public InvalidContainerStateException(string containerId, string currentState, string expectedState)
            : base($"Container '{containerId}' is in state '{currentState}' but expected '{expectedState}'", containerId)
        {
            CurrentState = currentState;
            ExpectedState = expectedState;
            ErrorCode = ErrorCodes.Container.InvalidState;
        }
    }
}
```

### Image Operation Exceptions

```csharp
namespace Ductus.FluentDocker.Exceptions
{
    public class ImageException : FluentDockerException
    {
        public string ImageName { get; set; }
        public string ImageTag { get; set; }

        public ImageException(string message, string imageName = null)
            : base(message)
        {
            ImageName = imageName;
            ErrorCode = ErrorCodes.Image.General;
        }
    }

    public class ImageNotFoundException : ImageException
    {
        public ImageNotFoundException(string imageName)
            : base($"Image '{imageName}' not found", imageName)
        {
            ErrorCode = ErrorCodes.Image.NotFound;
            StatusCode = 404;
        }
    }

    public class ImagePullException : ImageException
    {
        public ImagePullException(string imageName, string reason)
            : base($"Failed to pull image '{imageName}': {reason}", imageName)
        {
            ErrorCode = ErrorCodes.Image.PullFailed;
            IsTransient = true;  // Network issues are common
        }
    }

    public class ImageBuildException : ImageException
    {
        public string BuildContext { get; set; }
        public string Dockerfile { get; set; }

        public ImageBuildException(string imageName, string reason)
            : base($"Failed to build image '{imageName}': {reason}", imageName)
        {
            ErrorCode = ErrorCodes.Image.BuildFailed;
        }
    }
}
```

### Network and Volume Exceptions

```csharp
namespace Ductus.FluentDocker.Exceptions
{
    public class NetworkException : FluentDockerException
    {
        public string NetworkId { get; set; }
        public string NetworkName { get; set; }

        public NetworkException(string message, string networkId = null)
            : base(message)
        {
            NetworkId = networkId;
            ErrorCode = ErrorCodes.Network.General;
        }
    }

    public class NetworkNotFoundException : NetworkException
    {
        public NetworkNotFoundException(string networkId)
            : base($"Network '{networkId}' not found", networkId)
        {
            ErrorCode = ErrorCodes.Network.NotFound;
            StatusCode = 404;
        }
    }

    public class VolumeException : FluentDockerException
    {
        public string VolumeId { get; set; }
        public string VolumeName { get; set; }

        public VolumeException(string message, string volumeId = null)
            : base(message)
        {
            VolumeId = volumeId;
            ErrorCode = ErrorCodes.Volume.General;
        }
    }

    public class VolumeNotFoundException : VolumeException
    {
        public VolumeNotFoundException(string volumeId)
            : base($"Volume '{volumeId}' not found", volumeId)
        {
            ErrorCode = ErrorCodes.Volume.NotFound;
            StatusCode = 404;
        }
    }
}
```

### Compose Exceptions

```csharp
namespace Ductus.FluentDocker.Exceptions
{
    public class ComposeException : FluentDockerException
    {
        public string ComposeFile { get; set; }
        public string ProjectName { get; set; }

        public ComposeException(string message)
            : base(message)
        {
            ErrorCode = ErrorCodes.Compose.General;
        }
    }

    public class ComposeFileNotFoundException : ComposeException
    {
        public ComposeFileNotFoundException(string composeFile)
            : base($"Compose file '{composeFile}' not found")
        {
            ComposeFile = composeFile;
            ErrorCode = ErrorCodes.Compose.FileNotFound;
        }
    }

    public class ComposeValidationException : ComposeException
    {
        public IList<string> ValidationErrors { get; set; }

        public ComposeValidationException(string message, IList<string> validationErrors)
            : base(message)
        {
            ValidationErrors = validationErrors;
            ErrorCode = ErrorCodes.Compose.ValidationFailed;
        }
    }

    public class ComposeUpException : ComposeException
    {
        public ComposeUpException(string message, string projectName = null)
            : base($"Failed to bring up compose project: {message}")
        {
            ProjectName = projectName;
            ErrorCode = ErrorCodes.Compose.UpFailed;
        }
    }
}
```

### Configuration and Validation Exceptions

```csharp
namespace Ductus.FluentDocker.Exceptions
{
    public class ConfigurationException : FluentDockerException
    {
        public string ConfigurationKey { get; set; }
        public object InvalidValue { get; set; }

        public ConfigurationException(string message)
            : base(message)
        {
            ErrorCode = ErrorCodes.Configuration.General;
        }

        public ConfigurationException(string configKey, string message)
            : base($"Configuration error for '{configKey}': {message}")
        {
            ConfigurationKey = configKey;
            ErrorCode = ErrorCodes.Configuration.InvalidValue;
        }
    }

    public class ValidationException : FluentDockerException
    {
        public IList<ValidationError> Errors { get; set; }

        public ValidationException(string message, IList<ValidationError> errors = null)
            : base(message)
        {
            Errors = errors ?? new List<ValidationError>();
            ErrorCode = ErrorCodes.Validation.Failed;
        }

        public ValidationException(ValidationError error)
            : base(error.Message)
        {
            Errors = new List<ValidationError> { error };
            ErrorCode = ErrorCodes.Validation.Failed;
        }
    }

    public class ValidationError
    {
        public string Property { get; set; }
        public string Message { get; set; }
        public object AttemptedValue { get; set; }
        public string ErrorCode { get; set; }

        public ValidationError(string property, string message, object attemptedValue = null)
        {
            Property = property;
            Message = message;
            AttemptedValue = attemptedValue;
        }
    }
}
```

---

## Error Codes

### Error Code Structure

```csharp
namespace Ductus.FluentDocker.Exceptions
{
    /// <summary>
    /// Hierarchical error codes for programmatic error handling.
    /// Format: CATEGORY.SUBCATEGORY.ERROR (e.g., "DRIVER.NOTFOUND")
    /// </summary>
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
            public const string RegistrationFailed = "DRIVER.REGISTRATION_FAILED";
            public const string SelectionFailed = "DRIVER.SELECTION_FAILED";
            public const string InitializationFailed = "DRIVER.INITIALIZATION_FAILED";
        }

        public static class Container
        {
            public const string General = "CONTAINER.GENERAL";
            public const string NotFound = "CONTAINER.NOT_FOUND";
            public const string CreationFailed = "CONTAINER.CREATION_FAILED";
            public const string StartFailed = "CONTAINER.START_FAILED";
            public const string StopFailed = "CONTAINER.STOP_FAILED";
            public const string RemoveFailed = "CONTAINER.REMOVE_FAILED";
            public const string InvalidState = "CONTAINER.INVALID_STATE";
            public const string AlreadyExists = "CONTAINER.ALREADY_EXISTS";
            public const string InspectFailed = "CONTAINER.INSPECT_FAILED";
            public const string ExecFailed = "CONTAINER.EXEC_FAILED";
        }

        public static class Image
        {
            public const string General = "IMAGE.GENERAL";
            public const string NotFound = "IMAGE.NOT_FOUND";
            public const string PullFailed = "IMAGE.PULL_FAILED";
            public const string BuildFailed = "IMAGE.BUILD_FAILED";
            public const string RemoveFailed = "IMAGE.REMOVE_FAILED";
            public const string TagFailed = "IMAGE.TAG_FAILED";
            public const string InvalidReference = "IMAGE.INVALID_REFERENCE";
        }

        public static class Network
        {
            public const string General = "NETWORK.GENERAL";
            public const string NotFound = "NETWORK.NOT_FOUND";
            public const string CreationFailed = "NETWORK.CREATION_FAILED";
            public const string RemoveFailed = "NETWORK.REMOVE_FAILED";
            public const string ConnectFailed = "NETWORK.CONNECT_FAILED";
            public const string DisconnectFailed = "NETWORK.DISCONNECT_FAILED";
            public const string AlreadyExists = "NETWORK.ALREADY_EXISTS";
        }

        public static class Volume
        {
            public const string General = "VOLUME.GENERAL";
            public const string NotFound = "VOLUME.NOT_FOUND";
            public const string CreationFailed = "VOLUME.CREATION_FAILED";
            public const string RemoveFailed = "VOLUME.REMOVE_FAILED";
            public const string InUse = "VOLUME.IN_USE";
            public const string AlreadyExists = "VOLUME.ALREADY_EXISTS";
        }

        public static class Compose
        {
            public const string General = "COMPOSE.GENERAL";
            public const string FileNotFound = "COMPOSE.FILE_NOT_FOUND";
            public const string ValidationFailed = "COMPOSE.VALIDATION_FAILED";
            public const string UpFailed = "COMPOSE.UP_FAILED";
            public const string DownFailed = "COMPOSE.DOWN_FAILED";
            public const string BuildFailed = "COMPOSE.BUILD_FAILED";
        }

        public static class Configuration
        {
            public const string General = "CONFIGURATION.GENERAL";
            public const string InvalidValue = "CONFIGURATION.INVALID_VALUE";
            public const string MissingRequired = "CONFIGURATION.MISSING_REQUIRED";
        }

        public static class Validation
        {
            public const string Failed = "VALIDATION.FAILED";
            public const string RequiredField = "VALIDATION.REQUIRED_FIELD";
            public const string InvalidFormat = "VALIDATION.INVALID_FORMAT";
            public const string OutOfRange = "VALIDATION.OUT_OF_RANGE";
        }
    }
}
```

---

## Error Context

### ErrorContext Class

```csharp
namespace Ductus.FluentDocker.Exceptions
{
    /// <summary>
    /// Diagnostic context attached to exceptions for troubleshooting.
    /// </summary>
    public class ErrorContext
    {
        /// <summary>
        /// Unique operation identifier for correlation.
        /// </summary>
        public string OperationId { get; set; }

        /// <summary>
        /// Driver ID where error occurred.
        /// </summary>
        public string DriverId { get; set; }

        /// <summary>
        /// Docker/container host where error occurred.
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// Operation that was being performed.
        /// </summary>
        public string Operation { get; set; }

        /// <summary>
        /// Resource ID (container, image, network, volume ID).
        /// </summary>
        public string ResourceId { get; set; }

        /// <summary>
        /// Resource type (container, image, network, volume).
        /// </summary>
        public string ResourceType { get; set; }

        /// <summary>
        /// Exit code from process execution (if applicable).
        /// </summary>
        public int? ExitCode { get; set; }

        /// <summary>
        /// Standard output from command execution.
        /// </summary>
        public string StdOut { get; set; }

        /// <summary>
        /// Standard error from command execution.
        /// </summary>
        public string StdErr { get; set; }

        /// <summary>
        /// Command that was executed (if applicable).
        /// </summary>
        public string Command { get; set; }

        /// <summary>
        /// Timestamp when error occurred.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Additional custom properties.
        /// </summary>
        public IDictionary<string, object> Properties { get; set; }

        public ErrorContext()
        {
            Timestamp = DateTime.UtcNow;
            OperationId = Guid.NewGuid().ToString("N");
            Properties = new Dictionary<string, object>();
        }

        public bool HasContext =>
            !string.IsNullOrEmpty(DriverId) ||
            !string.IsNullOrEmpty(Host) ||
            !string.IsNullOrEmpty(Operation) ||
            !string.IsNullOrEmpty(ResourceId);

        public override string ToString()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(OperationId))
                parts.Add($"OpId={OperationId}");
            if (!string.IsNullOrEmpty(DriverId))
                parts.Add($"Driver={DriverId}");
            if (!string.IsNullOrEmpty(Host))
                parts.Add($"Host={Host}");
            if (!string.IsNullOrEmpty(Operation))
                parts.Add($"Op={Operation}");
            if (!string.IsNullOrEmpty(ResourceType) && !string.IsNullOrEmpty(ResourceId))
                parts.Add($"{ResourceType}={ResourceId}");
            if (ExitCode.HasValue)
                parts.Add($"ExitCode={ExitCode}");

            return string.Join(", ", parts);
        }

        public static ErrorContext FromDriverContext(DriverContext driverContext, string operation)
        {
            return new ErrorContext
            {
                DriverId = driverContext.Preferences?.PreferredDriverId,
                Host = driverContext.Host?.ToString(),
                Operation = operation
            };
        }
    }
}
```

---

## CommandResponse Enhancement

### Enhanced CommandResponse<T>

```csharp
namespace Ductus.FluentDocker.Commands
{
    /// <summary>
    /// Enhanced command response with error context.
    /// </summary>
    public sealed class CommandResponse<T>
    {
        public bool Success { get; }
        public IList<string> Log { get; }
        public string Error { get; }
        public T Data { get; }

        // NEW: Error context
        public ErrorContext ErrorContext { get; }

        // NEW: Error code
        public string ErrorCode { get; }

        public CommandResponse(bool success, IList<string> log, string error = "", T data = default(T),
            ErrorContext errorContext = null, string errorCode = null)
        {
            Success = success;
            Log = log ?? new List<string>();
            Error = error ?? string.Empty;
            Data = data;
            ErrorContext = errorContext;
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Throws an exception if the response indicates failure.
        /// </summary>
        public CommandResponse<T> ThrowIfFailed()
        {
            if (!Success)
            {
                throw CreateException();
            }
            return this;
        }

        /// <summary>
        /// Creates an appropriate exception from this response.
        /// </summary>
        public FluentDockerException CreateException()
        {
            var message = !string.IsNullOrEmpty(Error) ? Error : "Command execution failed";

            var ex = new FluentDockerException(message)
            {
                ErrorCode = ErrorCode ?? ErrorCodes.Unknown,
                Context = ErrorContext ?? new ErrorContext()
            };

            return ex;
        }

        /// <summary>
        /// Maps error response to success response with different type.
        /// </summary>
        public CommandResponse<TNew> MapError<TNew>(TNew defaultValue = default(TNew))
        {
            return new CommandResponse<TNew>(
                success: false,
                log: Log,
                error: Error,
                data: defaultValue,
                errorContext: ErrorContext,
                errorCode: ErrorCode
            );
        }

        /// <summary>
        /// Maps successful response to different type.
        /// </summary>
        public CommandResponse<TNew> Map<TNew>(Func<T, TNew> mapper)
        {
            if (!Success)
                return MapError<TNew>();

            try
            {
                var newData = mapper(Data);
                return new CommandResponse<TNew>(
                    success: true,
                    log: Log,
                    data: newData
                );
            }
            catch (Exception ex)
            {
                return new CommandResponse<TNew>(
                    success: false,
                    log: Log,
                    error: ex.Message,
                    errorContext: new ErrorContext
                    {
                        Operation = "ResponseMapping"
                    }
                );
            }
        }
    }
}
```

---

## Error Handling Patterns

### 1. Driver Layer Pattern

**Drivers return CommandResponse<T> with error context:**

```csharp
public class DockerCliContainerDriver : IContainerDriver
{
    public CommandResponse<string> Start(DriverContext context, string containerId)
    {
        var errorContext = ErrorContext.FromDriverContext(context, "Container.Start");
        errorContext.ResourceType = "container";
        errorContext.ResourceId = containerId;

        try
        {
            var args = $"{context.Host.RenderBaseArgs(context.Certificates)}";
            var result = new ProcessExecutor<StringResponseParser, string>(
                "docker".ResolveBinary(),
                $"{args} start {containerId}"
            ).Execute();

            // Enhance response with context
            if (!result.Success)
            {
                errorContext.ExitCode = result.ErrorContext?.ExitCode;
                errorContext.StdErr = result.Error;
                errorContext.Command = $"docker start {containerId}";

                return new CommandResponse<string>(
                    success: false,
                    log: result.Log,
                    error: result.Error,
                    errorContext: errorContext,
                    errorCode: ErrorCodes.Container.StartFailed
                );
            }

            return result;
        }
        catch (Exception ex)
        {
            return new CommandResponse<string>(
                success: false,
                log: new List<string>(),
                error: ex.Message,
                errorContext: errorContext,
                errorCode: ErrorCodes.Container.StartFailed
            );
        }
    }
}
```

### 2. Service Layer Pattern

**Services throw typed exceptions with context:**

```csharp
public class DockerContainerService : ServiceBase, IContainerService
{
    public override void Start()
    {
        if (State == ServiceRunningState.Running)
            return;

        var containerDriver = _kernel.SysCtl<IContainerDriver>(_driverId);
        var response = containerDriver.Start(_context, Id);

        if (!response.Success)
        {
            // Throw typed exception with full context
            throw new ContainerStartException(Id, response.Error)
            {
                Context = response.ErrorContext,
                ErrorCode = response.ErrorCode,
                IsTransient = IsTransientError(response)
            };
        }

        State = ServiceRunningState.Running;
        OnStateChange(ServiceRunningState.Running);
    }

    private bool IsTransientError(CommandResponse<string> response)
    {
        // Check if error is transient (network, daemon restart, etc.)
        if (response.ErrorContext?.ExitCode == 137) return true;  // SIGKILL
        if (response.Error?.Contains("connection refused") == true) return true;
        return false;
    }
}
```

### 3. Builder Layer Pattern

**Builders validate early and throw descriptive exceptions:**

```csharp
public class ContainerBuilder : BaseBuilder<IContainerService>
{
    public override IContainerService Build()
    {
        // Validation with typed exceptions
        ValidateConfiguration();

        try
        {
            var context = CreateDriverContext();
            var createParams = _config.ToCreateParams();
            var response = _kernel.CreateContainer(createParams, context);

            if (!response.Success)
            {
                throw new ContainerCreationException(
                    $"Failed to create container from image '{_config.Image}': {response.Error}"
                )
                {
                    Context = response.ErrorContext,
                    ErrorCode = response.ErrorCode,
                    CreateParams = createParams
                };
            }

            return new DockerContainerService(
                id: response.Data,
                image: _config.Image,
                kernel: _kernel,
                context: context
            );
        }
        catch (ContainerCreationException)
        {
            throw;  // Re-throw typed exceptions
        }
        catch (Exception ex)
        {
            throw new ContainerCreationException(
                $"Unexpected error creating container from image '{_config.Image}': {ex.Message}",
                _config.ToCreateParams()
            );
        }
    }

    private void ValidateConfiguration()
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrEmpty(_config.Image))
        {
            errors.Add(new ValidationError(
                nameof(_config.Image),
                "Image is required",
                _config.Image
            ));
        }

        if (_config.CreateParams.PublishAllPorts && _config.CreateParams.PortBindings?.Any() == true)
        {
            errors.Add(new ValidationError(
                nameof(_config.CreateParams.PublishAllPorts),
                "Cannot specify both PublishAllPorts and explicit port bindings",
                true
            ));
        }

        if (errors.Any())
        {
            throw new ValidationException("Container configuration validation failed", errors);
        }
    }
}
```

### 4. Kernel Layer Pattern

**Kernel provides high-level operations with driver selection fallback:**

```csharp
public class FluentDockerKernel : IFluentDockerKernel
{
    public CommandResponse<string> CreateContainer(
        ContainerCreateParams createParams,
        DriverContext context = null)
    {
        context ??= new DriverContext();
        var errorContext = ErrorContext.FromDriverContext(context, "Kernel.CreateContainer");

        try
        {
            // Select driver
            var driver = _selector.SelectDriver(context, _registry);
            errorContext.DriverId = GetDriverId(driver);

            // Execute with selected driver
            var response = driver.Containers.Create(context, createParams);

            // Add kernel context
            if (response.ErrorContext != null)
            {
                response.ErrorContext.OperationId = errorContext.OperationId;
            }

            return response;
        }
        catch (DriverNotFoundException ex)
        {
            return new CommandResponse<string>(
                success: false,
                log: new List<string>(),
                error: ex.Message,
                errorContext: errorContext,
                errorCode: ErrorCodes.Driver.NotFound
            );
        }
        catch (DriverSelectionException ex)
        {
            return new CommandResponse<string>(
                success: false,
                log: new List<string>(),
                error: ex.Message,
                errorContext: errorContext,
                errorCode: ErrorCodes.Driver.SelectionFailed
            );
        }
        catch (Exception ex)
        {
            return new CommandResponse<string>(
                success: false,
                log: new List<string>(),
                error: $"Unexpected error: {ex.Message}",
                errorContext: errorContext,
                errorCode: ErrorCodes.Unknown
            );
        }
    }

    private string GetDriverId(IDriver driver)
    {
        // Find driver ID from registry
        foreach (var kvp in _registry.GetAllDrivers())
        {
            if (kvp.Value == driver)
                return kvp.Key;
        }
        return null;
    }
}
```

---

## Retry and Recovery Mechanisms

### Retry Policy

```csharp
namespace Ductus.FluentDocker.Resilience
{
    public class RetryPolicy
    {
        public int MaxAttempts { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
        public double BackoffMultiplier { get; set; } = 2.0;
        public Func<Exception, bool> ShouldRetry { get; set; }

        public static RetryPolicy Default => new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 2.0,
            ShouldRetry = ex => ex is FluentDockerException fde && fde.IsTransient
        };

        public static RetryPolicy NoRetry => new RetryPolicy
        {
            MaxAttempts = 1
        };
    }

    public static class RetryExecutor
    {
        public static async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            RetryPolicy policy,
            CancellationToken cancellationToken = default)
        {
            var attempt = 0;
            var delay = policy.InitialDelay;

            while (true)
            {
                attempt++;

                try
                {
                    return await operation();
                }
                catch (Exception ex) when (attempt < policy.MaxAttempts &&
                    (policy.ShouldRetry?.Invoke(ex) ?? false))
                {
                    // Log retry attempt
                    Logger.Log($"Attempt {attempt} failed, retrying after {delay.TotalSeconds}s: {ex.Message}");

                    await Task.Delay(delay, cancellationToken);

                    // Exponential backoff
                    delay = TimeSpan.FromMilliseconds(
                        Math.Min(delay.TotalMilliseconds * policy.BackoffMultiplier,
                                 policy.MaxDelay.TotalMilliseconds)
                    );
                }
            }
        }

        public static T Execute<T>(
            Func<T> operation,
            RetryPolicy policy)
        {
            return ExecuteAsync(() => Task.FromResult(operation()), policy).Result;
        }
    }
}
```

### Usage with Retry

```csharp
// In driver implementation
public CommandResponse<string> PullImage(DriverContext context, string image)
{
    var policy = context.RetryPolicy ?? RetryPolicy.Default;

    return RetryExecutor.Execute(() =>
    {
        var result = ExecutePull(context, image);

        if (!result.Success)
        {
            throw new ImagePullException(image, result.Error)
            {
                Context = result.ErrorContext,
                IsTransient = IsNetworkError(result.Error)
            };
        }

        return result;
    }, policy);
}
```

---

## Logging and Observability

### Structured Logging

```csharp
namespace Ductus.FluentDocker.Logging
{
    public enum LogLevel
    {
        Trace,
        Debug,
        Information,
        Warning,
        Error,
        Critical
    }

    public interface IFluentDockerLogger
    {
        void Log(LogLevel level, string message, ErrorContext context = null, Exception exception = null);
        void LogTrace(string message, ErrorContext context = null);
        void LogDebug(string message, ErrorContext context = null);
        void LogInformation(string message, ErrorContext context = null);
        void LogWarning(string message, ErrorContext context = null, Exception exception = null);
        void LogError(string message, ErrorContext context = null, Exception exception = null);
        void LogCritical(string message, ErrorContext context = null, Exception exception = null);
    }

    public class DefaultLogger : IFluentDockerLogger
    {
        public void Log(LogLevel level, string message, ErrorContext context = null, Exception exception = null)
        {
            var contextStr = context != null ? $" [{context}]" : "";
            var exStr = exception != null ? $" Exception: {exception}" : "";

            var logMessage = $"[{level}]{contextStr} {message}{exStr}";

            Trace.WriteLine(logMessage, Constants.DebugCategory);
        }

        public void LogTrace(string message, ErrorContext context = null) =>
            Log(LogLevel.Trace, message, context);

        public void LogDebug(string message, ErrorContext context = null) =>
            Log(LogLevel.Debug, message, context);

        public void LogInformation(string message, ErrorContext context = null) =>
            Log(LogLevel.Information, message, context);

        public void LogWarning(string message, ErrorContext context = null, Exception exception = null) =>
            Log(LogLevel.Warning, message, context, exception);

        public void LogError(string message, ErrorContext context = null, Exception exception = null) =>
            Log(LogLevel.Error, message, context, exception);

        public void LogCritical(string message, ErrorContext context = null, Exception exception = null) =>
            Log(LogLevel.Critical, message, context, exception);
    }

    public static class LoggerFactory
    {
        private static IFluentDockerLogger _logger = new DefaultLogger();

        public static IFluentDockerLogger GetLogger() => _logger;

        public static void SetLogger(IFluentDockerLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
    }
}
```

### Metrics and Telemetry

```csharp
namespace Ductus.FluentDocker.Telemetry
{
    public interface IFluentDockerMetrics
    {
        void RecordOperation(string operation, bool success, TimeSpan duration, ErrorContext context = null);
        void RecordException(Exception exception, ErrorContext context = null);
        void RecordDriverOperation(string driverId, string operation, bool success, TimeSpan duration);
    }

    public class DefaultMetrics : IFluentDockerMetrics
    {
        public void RecordOperation(string operation, bool success, TimeSpan duration, ErrorContext context = null)
        {
            var contextStr = context != null ? $" [{context}]" : "";
            var status = success ? "Success" : "Failed";
            Trace.WriteLine($"[Metric] Operation={operation} Status={status} Duration={duration.TotalMilliseconds}ms{contextStr}");
        }

        public void RecordException(Exception exception, ErrorContext context = null)
        {
            var contextStr = context != null ? $" [{context}]" : "";
            Trace.WriteLine($"[Metric] Exception={exception.GetType().Name} Message={exception.Message}{contextStr}");
        }

        public void RecordDriverOperation(string driverId, string operation, bool success, TimeSpan duration)
        {
            var status = success ? "Success" : "Failed";
            Trace.WriteLine($"[Metric] Driver={driverId} Operation={operation} Status={status} Duration={duration.TotalMilliseconds}ms");
        }
    }

    public static class MetricsFactory
    {
        private static IFluentDockerMetrics _metrics = new DefaultMetrics();

        public static IFluentDockerMetrics GetMetrics() => _metrics;

        public static void SetMetrics(IFluentDockerMetrics metrics)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        }
    }
}
```

---

## Migration Strategy from v2.x.x to v3.0.0

### Phase 1: Add New Exception Types (Non-Breaking)

**Add to codebase without changing existing code:**

1. Create new exception hierarchy in `Ductus.FluentDocker.Exceptions/`
2. Create `ErrorCodes` class
3. Create `ErrorContext` class
4. Add to project but don't use yet

**Timeline**: Week 1

### Phase 2: Enhance CommandResponse (Non-Breaking)

**Add optional parameters:**

```csharp
// Old constructor still works
public CommandResponse(bool success, IList<string> log, string error = "", T data = default(T))
    : this(success, log, error, data, null, null)
{
}

// New constructor with optional context
public CommandResponse(bool success, IList<string> log, string error, T data,
    ErrorContext errorContext, string errorCode)
{
    // Implementation
}
```

**Timeline**: Week 1

### Phase 3: Update Driver Layer (New Code)

**New drivers use new error handling:**

```csharp
// DockerCliDriver uses new error patterns
public CommandResponse<string> Start(DriverContext context, string containerId)
{
    var errorContext = ErrorContext.FromDriverContext(context, "Container.Start");
    // Use new error handling
}
```

**Timeline**: Weeks 2-3 (during driver implementation)

### Phase 4: Update Services Layer (Breaking)

**Services throw typed exceptions:**

```csharp
// OLD (v2.x.x)
if (!result.Success)
    throw new FluentDockerException($"Failed to start container {Name} log: {result}");

// NEW (v3.0.0)
if (!result.Success)
{
    throw new ContainerStartException(Id, result.Error)
    {
        Context = result.ErrorContext,
        ErrorCode = result.ErrorCode
    };
}
```

**Migration for users:**

```csharp
// OLD code
try {
    container.Start();
}
catch (FluentDockerException ex) {
    // Handle generic exception
}

// NEW code (more specific handling)
try {
    container.Start();
}
catch (ContainerStartException ex) {
    // Handle specific start failure
    Console.WriteLine($"Failed to start {ex.ContainerId}: {ex.Message}");
    Console.WriteLine($"Error code: {ex.ErrorCode}");
    Console.WriteLine($"Context: {ex.Context}");
}
catch (FluentDockerException ex) {
    // Catch-all for other errors
}
```

**Timeline**: Week 4

### Phase 5: Add Retry Support (New Feature)

**Add retry policies to DriverContext:**

```csharp
public class DriverContext
{
    // NEW in v3.0.0
    public RetryPolicy RetryPolicy { get; set; }
}

// Usage
var context = new DriverContext
{
    RetryPolicy = RetryPolicy.Default
};

var result = kernel.PullImage("nginx", context);  // Auto-retries on transient errors
```

**Timeline**: Week 5

### Phase 6: Add Logging/Metrics (New Feature)

**Allow custom logger injection:**

```csharp
// Setup custom logger
LoggerFactory.SetLogger(new MyCustomLogger());
MetricsFactory.SetMetrics(new MyCustomMetrics());

// FluentDocker will use custom logger/metrics
```

**Timeline**: Week 5

---

## Exception Handling Best Practices

### For Library Users

**1. Catch Specific Exceptions:**

```csharp
// GOOD - Specific handling
try {
    container.Start();
}
catch (ContainerNotFoundException ex) {
    // Container doesn't exist - create it first
}
catch (ContainerStartException ex) {
    if (ex.IsTransient) {
        // Retry transient errors
    } else {
        // Log and fail
    }
}

// AVOID - Generic catch
catch (Exception ex) { }
```

**2. Use Error Codes for Programmatic Decisions:**

```csharp
try {
    container.Start();
}
catch (FluentDockerException ex) {
    switch (ex.ErrorCode) {
        case ErrorCodes.Container.NotFound:
            // Recreate container
            break;
        case ErrorCodes.Container.InvalidState:
            // Stop then start
            break;
        default:
            throw;
    }
}
```

**3. Leverage Error Context:**

```csharp
catch (FluentDockerException ex) {
    Console.WriteLine($"Operation: {ex.Context.Operation}");
    Console.WriteLine($"Driver: {ex.Context.DriverId}");
    Console.WriteLine($"Host: {ex.Context.Host}");
    Console.WriteLine($"Exit Code: {ex.Context.ExitCode}");

    // Log full context for diagnostics
    logger.Error($"FluentDocker error: {ex}", ex.Context);
}
```

**4. Check Transient Errors:**

```csharp
try {
    image.Pull();
}
catch (ImagePullException ex) when (ex.IsTransient) {
    // Retry transient errors (network issues)
    await Task.Delay(TimeSpan.FromSeconds(5));
    image.Pull();
}
```

### For Driver Implementers

**1. Always Provide Error Context:**

```csharp
public CommandResponse<string> SomeOperation(DriverContext context, string resourceId)
{
    var errorContext = new ErrorContext
    {
        DriverId = _driverId,
        Operation = "SomeOperation",
        ResourceType = "container",
        ResourceId = resourceId
    };

    try {
        // Operation
    }
    catch (Exception ex) {
        return new CommandResponse<string>(
            success: false,
            log: new List<string>(),
            error: ex.Message,
            errorContext: errorContext,
            errorCode: DetermineErrorCode(ex)
        );
    }
}
```

**2. Set IsTransient Flag:**

```csharp
private bool IsTransientError(Exception ex, CommandResponse response)
{
    // Network errors
    if (ex is System.Net.Sockets.SocketException) return true;
    if (response.Error?.Contains("connection refused") == true) return true;

    // Daemon restart
    if (response.ErrorContext?.ExitCode == 137) return true;  // SIGKILL

    // Resource temporarily unavailable
    if (response.Error?.Contains("resource temporarily unavailable") == true) return true;

    return false;
}
```

**3. Map to Appropriate Error Codes:**

```csharp
private string DetermineErrorCode(Exception ex, ProcessExecutionResult result)
{
    if (result.ExitCode == 1) return ErrorCodes.Container.NotFound;
    if (result.ExitCode == 125) return ErrorCodes.Container.CreationFailed;
    if (result.StdErr?.Contains("permission denied") == true) return ErrorCodes.Configuration.InvalidValue;

    return ErrorCodes.Container.General;
}
```

---

## Summary

### v3.0.0 Error Handling Improvements

✅ **Typed exception hierarchy** - 20+ specific exception types
✅ **Error codes** - Programmatic error identification
✅ **Error context** - Rich diagnostic information (OpId, driver, host, etc.)
✅ **Enhanced CommandResponse** - Includes error context and codes
✅ **Retry mechanisms** - Built-in retry policies for transient errors
✅ **Structured logging** - Log levels, context, metrics
✅ **Transient error detection** - IsTransient flag for retry decisions
✅ **Migration friendly** - Backward compatible with gradual migration
✅ **Better observability** - Correlation IDs, operation traces, metrics

### Migration Effort

| Component | Effort | Changes Required |
|-----------|--------|------------------|
| Library code | **High** | Implement new exception types, error handling |
| User catch blocks | **Low** | Optional - can catch FluentDockerException still |
| Error code usage | **None** | Optional new feature |
| Logging/metrics | **None** | Optional new feature |
| Retry policies | **None** | Optional new feature |

The strategy provides **significant improvements** while maintaining **backward compatibility** for most user code.
