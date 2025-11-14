# FluentDocker Kernel and Driver Layer - Implementation Plan

## Overview

This document provides a detailed, step-by-step implementation plan for building the FluentDocker Kernel and Driver Layer infrastructure. This is Phase 1-2 of the overall refactoring effort.

---

## Project Structure

### New Folder Structure

```
Ductus.FluentDocker/
├── Drivers/                          # NEW: Driver layer
│   ├── Core/                         # Core driver abstractions
│   │   ├── IDriver.cs
│   │   ├── IContainerDriver.cs
│   │   ├── IImageDriver.cs
│   │   ├── INetworkDriver.cs
│   │   ├── IVolumeDriver.cs
│   │   ├── IComposeDriver.cs
│   │   ├── ISystemDriver.cs
│   │   └── IPodDriver.cs             # For Podman pods
│   ├── Models/                       # Driver-specific models
│   │   ├── DriverContext.cs
│   │   ├── DriverCapabilities.cs
│   │   ├── DriverType.cs
│   │   ├── RuntimeType.cs
│   │   ├── DriverHealthStatus.cs
│   │   ├── DriverPreferences.cs
│   │   ├── ContainerListFilter.cs
│   │   ├── ImageListFilter.cs
│   │   ├── NetworkListFilter.cs
│   │   ├── VolumeListFilter.cs
│   │   ├── ExecParams.cs
│   │   ├── AttachParams.cs
│   │   ├── LogOptions.cs
│   │   ├── ComposeUpParams.cs
│   │   ├── ComposeDownParams.cs
│   │   └── ...
│   ├── Docker/                       # Docker drivers
│   │   ├── Cli/                      # Docker CLI driver
│   │   │   ├── DockerCliDriver.cs
│   │   │   ├── DockerCliContainerDriver.cs
│   │   │   ├── DockerCliImageDriver.cs
│   │   │   ├── DockerCliNetworkDriver.cs
│   │   │   ├── DockerCliVolumeDriver.cs
│   │   │   ├── DockerComposeCliDriver.cs
│   │   │   └── DockerCliSystemDriver.cs
│   │   └── Api/                      # Docker API driver
│   │       ├── DockerApiDriver.cs
│   │       ├── DockerApiContainerDriver.cs
│   │       ├── DockerApiImageDriver.cs
│   │       ├── DockerApiNetworkDriver.cs
│   │       ├── DockerApiVolumeDriver.cs
│   │       ├── DockerApiComposeDriver.cs
│   │       └── DockerApiSystemDriver.cs
│   ├── Podman/                       # Podman drivers
│   │   └── Cli/                      # Podman CLI driver
│   │       ├── PodmanCliDriver.cs
│   │       ├── PodmanCliContainerDriver.cs
│   │       ├── PodmanCliImageDriver.cs
│   │       ├── PodmanCliNetworkDriver.cs
│   │       ├── PodmanCliVolumeDriver.cs
│   │       ├── PodmanComposeCliDriver.cs
│   │       ├── PodmanCliSystemDriver.cs
│   │       └── PodmanCliPodDriver.cs
│   └── Utils/                        # Driver utilities
│       ├── DriverHelpers.cs
│       ├── DriverLogger.cs
│       └── DriverException.cs
├── Kernel/                           # NEW: FluentDocker Kernel
│   ├── FluentDockerKernel.cs         # Main kernel singleton
│   ├── Registry/
│   │   ├── IDriverRegistry.cs
│   │   ├── DriverRegistry.cs
│   │   └── DriverRegistrationOptions.cs
│   ├── Selection/
│   │   ├── IDriverSelector.cs
│   │   ├── DefaultDriverSelector.cs
│   │   ├── DriverSelectionCriteria.cs
│   │   └── DriverScorer.cs
│   ├── Routing/
│   │   ├── DriverRouter.cs
│   │   └── DriverRoutingException.cs
│   ├── Events/
│   │   ├── EventBus.cs
│   │   ├── DriverEvent.cs
│   │   └── EventHandlers.cs
│   └── Capabilities/
│       ├── CapabilityMatrix.cs
│       └── FeatureDiscovery.cs
├── Commands/                         # MODIFIED: Becomes facade over drivers
│   ├── Client.cs
│   ├── Compose.cs
│   ├── Network.cs
│   └── ...
├── Services/                         # MODIFIED: Use Kernel
│   └── Impl/
│       ├── DockerHostService.cs
│       ├── DockerContainerService.cs
│       └── ...
└── Extensions/                       # NEW: Driver-specific extensions
    ├── Docker/
    │   └── DockerExtensions.cs
    └── Podman/
        ├── PodmanExtensions.cs
        └── PodBuilderExtensions.cs
```

---

## Phase 1: Core Driver Infrastructure (Week 1)

### Task 1.1: Create Driver Enums and Constants

**File**: `Ductus.FluentDocker/Drivers/Models/DriverType.cs`

```csharp
namespace Ductus.FluentDocker.Drivers.Models
{
    /// <summary>
    /// Specifies the type of driver implementation.
    /// </summary>
    public enum DriverType
    {
        /// <summary>
        /// Driver uses command-line interface (CLI) to interact with container runtime.
        /// </summary>
        CLI,

        /// <summary>
        /// Driver uses REST API or SDK to interact with container runtime.
        /// </summary>
        API,

        /// <summary>
        /// Driver uses both CLI and API based on operation type.
        /// </summary>
        Hybrid,

        /// <summary>
        /// Mock driver for testing purposes.
        /// </summary>
        Mock
    }
}
```

**File**: `Ductus.FluentDocker/Drivers/Models/RuntimeType.cs`

```csharp
namespace Ductus.FluentDocker.Drivers.Models
{
    /// <summary>
    /// Specifies the container runtime type.
    /// </summary>
    public enum RuntimeType
    {
        /// <summary>
        /// Automatically detect runtime.
        /// </summary>
        Auto,

        /// <summary>
        /// Docker container runtime.
        /// </summary>
        Docker,

        /// <summary>
        /// Podman container runtime.
        /// </summary>
        Podman,

        /// <summary>
        /// Containerd runtime.
        /// </summary>
        Containerd,

        /// <summary>
        /// CRI-O runtime.
        /// </summary>
        CriO
    }
}
```

**File**: `Ductus.FluentDocker/Drivers/Models/PreferredDriverType.cs`

```csharp
namespace Ductus.FluentDocker.Drivers.Models
{
    /// <summary>
    /// Specifies the preferred driver type for automatic driver selection.
    /// </summary>
    public enum PreferredDriverType
    {
        /// <summary>
        /// Automatically select best available driver.
        /// </summary>
        Auto,

        /// <summary>
        /// Prefer CLI-based drivers.
        /// </summary>
        CLI,

        /// <summary>
        /// Prefer API-based drivers.
        /// </summary>
        API,

        /// <summary>
        /// Prefer hybrid drivers.
        /// </summary>
        Hybrid
    }
}
```

### Task 1.2: Create DriverContext

**File**: `Ductus.FluentDocker/Drivers/Models/DriverContext.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Drivers.Models
{
    /// <summary>
    /// Execution context for driver operations.
    /// </summary>
    public class DriverContext
    {
        /// <summary>
        /// Docker host URI.
        /// </summary>
        public DockerUri Host { get; set; }

        /// <summary>
        /// TLS certificate paths for secure communication.
        /// </summary>
        public ICertificatePaths Certificates { get; set; }

        /// <summary>
        /// Sudo mechanism for privileged operations.
        /// </summary>
        public SudoMechanism SudoMechanism { get; set; } = SudoMechanism.None;

        /// <summary>
        /// Operation timeout.
        /// </summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>
        /// Driver selection preferences.
        /// </summary>
        public DriverPreferences Preferences { get; set; }

        /// <summary>
        /// Cancellation token for async operations.
        /// </summary>
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

        /// <summary>
        /// Additional metadata for driver-specific use.
        /// </summary>
        public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Creates a default driver context.
        /// </summary>
        public DriverContext()
        {
            Preferences = new DriverPreferences();
        }

        /// <summary>
        /// Creates a driver context from a DockerUri.
        /// </summary>
        public static DriverContext FromHost(DockerUri host, ICertificatePaths certificates = null)
        {
            return new DriverContext
            {
                Host = host,
                Certificates = certificates
            };
        }
    }
}
```

**File**: `Ductus.FluentDocker/Drivers/Models/DriverPreferences.cs`

```csharp
using System.Collections.Generic;

namespace Ductus.FluentDocker.Drivers.Models
{
    /// <summary>
    /// Driver selection preferences.
    /// </summary>
    public class DriverPreferences
    {
        /// <summary>
        /// Preferred driver type (CLI, API, etc.).
        /// </summary>
        public PreferredDriverType PreferredType { get; set; } = PreferredDriverType.Auto;

        /// <summary>
        /// Allow fallback to another driver if preferred is unavailable.
        /// </summary>
        public bool AllowFallback { get; set; } = true;

        /// <summary>
        /// Target container runtime.
        /// </summary>
        public RuntimeType TargetRuntime { get; set; } = RuntimeType.Auto;

        /// <summary>
        /// Ordered list of preferred driver names. First available driver is selected.
        /// </summary>
        public IList<string> PreferredDrivers { get; set; } = new List<string>();

        /// <summary>
        /// Minimum priority for driver selection (0-1000).
        /// </summary>
        public int MinimumPriority { get; set; } = 0;

        /// <summary>
        /// Whether to cache driver selection for this context.
        /// </summary>
        public bool CacheSelection { get; set; } = true;
    }
}
```

### Task 1.3: Create DriverCapabilities

**File**: `Ductus.FluentDocker/Drivers/Models/DriverCapabilities.cs`

```csharp
using System.Collections.Generic;

namespace Ductus.FluentDocker.Drivers.Models
{
    /// <summary>
    /// Describes the capabilities supported by a driver.
    /// </summary>
    public class DriverCapabilities
    {
        // Core resource types
        public bool SupportsContainers { get; set; } = false;
        public bool SupportsImages { get; set; } = false;
        public bool SupportsNetworks { get; set; } = false;
        public bool SupportsVolumes { get; set; } = false;
        public bool SupportsCompose { get; set; } = false;

        // Advanced Docker features
        public bool SupportsSwarm { get; set; } = false;
        public bool SupportsSecrets { get; set; } = false;
        public bool SupportsConfigs { get; set; } = false;
        public bool SupportsPlugins { get; set; } = false;
        public bool SupportsStacks { get; set; } = false;
        public bool SupportsServices { get; set; } = false;

        // Container features
        public bool SupportsHealthCheck { get; set; } = false;
        public bool SupportsExec { get; set; } = false;
        public bool SupportsAttach { get; set; } = false;
        public bool SupportsLogs { get; set; } = false;
        public bool SupportsStats { get; set; } = false;
        public bool SupportsTop { get; set; } = false;
        public bool SupportsCopy { get; set; } = false;
        public bool SupportsDiff { get; set; } = false;

        // Image features
        public bool SupportsBuild { get; set; } = false;
        public bool SupportsMultiPlatformBuild { get; set; } = false;
        public bool SupportsBuildx { get; set; } = false;
        public bool SupportsBuildCache { get; set; } = false;
        public bool SupportsImageImportExport { get; set; } = false;

        // Security features
        public bool SupportsContentTrust { get; set; } = false;
        public bool SupportsRootless { get; set; } = false;
        public bool SupportsSELinux { get; set; } = false;
        public bool SupportsAppArmor { get; set; } = false;
        public bool SupportsSeccomp { get; set; } = false;

        // Podman-specific features
        public bool SupportsPods { get; set; } = false;
        public bool SupportsKubeYaml { get; set; } = false;
        public bool SupportsSystemdGeneration { get; set; } = false;

        // Performance features
        public bool SupportsStreaming { get; set; } = false;
        public bool SupportsBulkOperations { get; set; } = false;
        public bool SupportsAsyncOperations { get; set; } = false;

        // Version constraints
        public string MinimumRuntimeVersion { get; set; }
        public string MaximumRuntimeVersion { get; set; }

        // Custom capabilities
        public ISet<string> CustomCapabilities { get; set; } = new HashSet<string>();

        /// <summary>
        /// Checks if the driver has a specific custom capability.
        /// </summary>
        public bool HasCapability(string capability)
        {
            return CustomCapabilities.Contains(capability);
        }

        /// <summary>
        /// Adds a custom capability.
        /// </summary>
        public void AddCapability(string capability)
        {
            CustomCapabilities.Add(capability);
        }
    }
}
```

### Task 1.4: Create DriverHealthStatus

**File**: `Ductus.FluentDocker/Drivers/Models/DriverHealthStatus.cs`

```csharp
using System;
using System.Collections.Generic;

namespace Ductus.FluentDocker.Drivers.Models
{
    /// <summary>
    /// Represents the health status of a driver.
    /// </summary>
    public class DriverHealthStatus
    {
        /// <summary>
        /// Whether the driver is healthy and available.
        /// </summary>
        public bool IsHealthy { get; set; }

        /// <summary>
        /// Status message.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Runtime version detected (e.g., "20.10.17" for Docker).
        /// </summary>
        public string RuntimeVersion { get; set; }

        /// <summary>
        /// API version supported (e.g., "1.41" for Docker).
        /// </summary>
        public string ApiVersion { get; set; }

        /// <summary>
        /// When the health check was performed.
        /// </summary>
        public DateTime CheckedAt { get; set; }

        /// <summary>
        /// Time taken to perform health check.
        /// </summary>
        public TimeSpan ResponseTime { get; set; }

        /// <summary>
        /// Additional health information.
        /// </summary>
        public IDictionary<string, string> AdditionalInfo { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Exception if health check failed.
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// Creates a healthy status.
        /// </summary>
        public static DriverHealthStatus Healthy(string message = "Driver is healthy")
        {
            return new DriverHealthStatus
            {
                IsHealthy = true,
                Message = message,
                CheckedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Creates an unhealthy status.
        /// </summary>
        public static DriverHealthStatus Unhealthy(string message, Exception exception = null)
        {
            return new DriverHealthStatus
            {
                IsHealthy = false,
                Message = message,
                Exception = exception,
                CheckedAt = DateTime.UtcNow
            };
        }
    }
}
```

### Task 1.5: Create Core Driver Interfaces

**File**: `Ductus.FluentDocker/Drivers/Core/IDriver.cs`

```csharp
using System;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Drivers.Core
{
    /// <summary>
    /// Base interface for all container runtime drivers.
    /// </summary>
    public interface IDriver : IDisposable
    {
        /// <summary>
        /// Unique name of the driver (e.g., "docker-cli", "docker-api", "podman-cli").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Driver version.
        /// </summary>
        string Version { get; }

        /// <summary>
        /// Type of driver implementation.
        /// </summary>
        DriverType Type { get; }

        /// <summary>
        /// Container runtime type.
        /// </summary>
        RuntimeType Runtime { get; }

        /// <summary>
        /// Driver for container operations.
        /// </summary>
        IContainerDriver Containers { get; }

        /// <summary>
        /// Driver for image operations.
        /// </summary>
        IImageDriver Images { get; }

        /// <summary>
        /// Driver for network operations.
        /// </summary>
        INetworkDriver Networks { get; }

        /// <summary>
        /// Driver for volume operations.
        /// </summary>
        IVolumeDriver Volumes { get; }

        /// <summary>
        /// Driver for compose operations.
        /// </summary>
        IComposeDriver Compose { get; }

        /// <summary>
        /// Driver for system operations (version, info, etc.).
        /// </summary>
        ISystemDriver System { get; }

        /// <summary>
        /// Gets the capabilities supported by this driver.
        /// </summary>
        DriverCapabilities GetCapabilities();

        /// <summary>
        /// Checks if the driver is available in the given context.
        /// </summary>
        bool IsAvailable(DriverContext context);

        /// <summary>
        /// Performs a health check on the driver.
        /// </summary>
        DriverHealthStatus HealthCheck(DriverContext context);
    }
}
```

**File**: `Ductus.FluentDocker/Drivers/Core/IContainerDriver.cs`

```csharp
using System.Collections.Generic;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Drivers.Core
{
    /// <summary>
    /// Driver interface for container operations.
    /// </summary>
    public interface IContainerDriver
    {
        // Container lifecycle
        CommandResponse<string> Create(DriverContext context, ContainerCreateParams createParams);
        CommandResponse<string> Start(DriverContext context, string containerId);
        CommandResponse<string> Stop(DriverContext context, string containerId, int? waitMs = null);
        CommandResponse<string> Pause(DriverContext context, string containerId);
        CommandResponse<string> Unpause(DriverContext context, string containerId);
        CommandResponse<string> Remove(DriverContext context, string containerId, bool force = false, bool removeVolumes = false);
        CommandResponse<string> Restart(DriverContext context, string containerId, int? waitMs = null);
        CommandResponse<string> Kill(DriverContext context, string containerId, string signal = "SIGKILL");

        // Container inspection
        CommandResponse<Container> Inspect(DriverContext context, string containerId);
        CommandResponse<IList<Container>> List(DriverContext context, bool all = false, params string[] filters);
        CommandResponse<Processes> Top(DriverContext context, string containerId, string psArgs = null);
        CommandResponse<IList<Diff>> Diff(DriverContext context, string containerId);
        CommandResponse<string> Logs(DriverContext context, string containerId, bool follow = false, bool timestamps = false);

        // Container execution
        CommandResponse<string> Execute(DriverContext context, string containerId, string command, params string[] args);

        // Container file operations
        CommandResponse<string> CopyTo(DriverContext context, string containerId, string hostPath, string containerPath);
        CommandResponse<string> CopyFrom(DriverContext context, string containerId, string containerPath, string hostPath);

        // Container resource management
        CommandResponse<string> Rename(DriverContext context, string containerId, string newName);
    }
}
```

**File**: `Ductus.FluentDocker/Drivers/Core/IImageDriver.cs`

```csharp
using System.Collections.Generic;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Model.Images;

namespace Ductus.FluentDocker.Drivers.Core
{
    /// <summary>
    /// Driver interface for image operations.
    /// </summary>
    public interface IImageDriver
    {
        CommandResponse<string> Pull(DriverContext context, string image, string authBase64 = null);
        CommandResponse<IList<string>> Build(DriverContext context, string imageName, string workingDirectory, string dockerFile = null, params string[] args);
        CommandResponse<IList<DockerImageRowResponse>> List(DriverContext context, bool all = false, params string[] filters);
        CommandResponse<ImageConfig> Inspect(DriverContext context, string imageId);
        CommandResponse<IList<string>> Remove(DriverContext context, string imageId, bool force = false, bool noPrune = false);
        CommandResponse<string> Tag(DriverContext context, string sourceImage, string targetImage);
    }
}
```

**File**: `Ductus.FluentDocker/Drivers/Core/INetworkDriver.cs`

```csharp
using System.Collections.Generic;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Model.Networks;

namespace Ductus.FluentDocker.Drivers.Core
{
    /// <summary>
    /// Driver interface for network operations.
    /// </summary>
    public interface INetworkDriver
    {
        CommandResponse<string> Create(DriverContext context, NetworkCreateParams createParams);
        CommandResponse<NetworkConfiguration> Inspect(DriverContext context, string networkId);
        CommandResponse<IList<NetworkRow>> List(DriverContext context, params string[] filters);
        CommandResponse<string> Remove(DriverContext context, string networkId);
        CommandResponse<string> Connect(DriverContext context, string networkId, string containerId);
        CommandResponse<string> Disconnect(DriverContext context, string networkId, string containerId, bool force = false);
    }
}
```

**File**: `Ductus.FluentDocker/Drivers/Core/IVolumeDriver.cs`

```csharp
using System.Collections.Generic;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Model.Volumes;

namespace Ductus.FluentDocker.Drivers.Core
{
    /// <summary>
    /// Driver interface for volume operations.
    /// </summary>
    public interface IVolumeDriver
    {
        CommandResponse<Volume> Create(DriverContext context, string name = null, string driver = null, IDictionary<string, string> labels = null);
        CommandResponse<Volume> Inspect(DriverContext context, string volumeName);
        CommandResponse<IList<Volume>> List(DriverContext context, params string[] filters);
        CommandResponse<string> Remove(DriverContext context, string volumeName, bool force = false);
    }
}
```

**File**: `Ductus.FluentDocker/Drivers/Core/IComposeDriver.cs`

```csharp
using System.Collections.Generic;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Model.Compose;

namespace Ductus.FluentDocker.Drivers.Core
{
    /// <summary>
    /// Driver interface for Docker Compose operations.
    /// </summary>
    public interface IComposeDriver
    {
        CommandResponse<string> Up(DriverContext context, ComposeUpParams upParams);
        CommandResponse<string> Down(DriverContext context, string workingDirectory, string[] composeFiles, string projectName = null, bool removeVolumes = false, bool removeImages = false);
        CommandResponse<string> Start(DriverContext context, string workingDirectory, string[] composeFiles, string projectName = null, string[] services = null);
        CommandResponse<string> Stop(DriverContext context, string workingDirectory, string[] composeFiles, string projectName = null, string[] services = null);
        CommandResponse<string> Restart(DriverContext context, string workingDirectory, string[] composeFiles, string projectName = null, string[] services = null);
        CommandResponse<string> Pause(DriverContext context, string workingDirectory, string[] composeFiles, string projectName = null, string[] services = null);
        CommandResponse<string> Unpause(DriverContext context, string workingDirectory, string[] composeFiles, string projectName = null, string[] services = null);
        CommandResponse<string> Build(DriverContext context, string workingDirectory, string[] composeFiles, string projectName = null, string[] services = null);
        CommandResponse<string> Pull(DriverContext context, string workingDirectory, string[] composeFiles, string projectName = null, string[] services = null);
        CommandResponse<IList<string>> Ps(DriverContext context, string workingDirectory, string[] composeFiles, string projectName = null);
        CommandResponse<string> Version(DriverContext context);
    }
}
```

**File**: `Ductus.FluentDocker/Drivers/Core/ISystemDriver.cs`

```csharp
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Drivers.Core
{
    /// <summary>
    /// Driver interface for system-level operations.
    /// </summary>
    public interface ISystemDriver
    {
        CommandResponse<VersionResponse> Version(DriverContext context);
        CommandResponse<bool> IsWindowsEngine(DriverContext context);
        CommandResponse<string> Ping(DriverContext context);
    }
}
```

---

## Phase 2: Kernel Infrastructure (Week 1)

### Task 2.1: Create Driver Registry

**File**: `Ductus.FluentDocker/Kernel/Registry/DriverRegistrationOptions.cs`

```csharp
using System;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Kernel.Registry
{
    /// <summary>
    /// Options for registering a driver.
    /// </summary>
    public class DriverRegistrationOptions
    {
        /// <summary>
        /// Priority for driver selection. Higher priority drivers are preferred.
        /// Range: 0-1000, Default: 100.
        /// </summary>
        public int Priority { get; set; } = 100;

        /// <summary>
        /// Whether this driver is the default for its runtime type.
        /// </summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// Whether to automatically initialize the driver on registration.
        /// </summary>
        public bool AutoInitialize { get; set; } = true;

        /// <summary>
        /// Custom availability checker function.
        /// </summary>
        public Func<DriverContext, bool> AvailabilityChecker { get; set; }

        /// <summary>
        /// Tags for driver categorization.
        /// </summary>
        public string[] Tags { get; set; } = Array.Empty<string>();
    }
}
```

**File**: `Ductus.FluentDocker/Kernel/Registry/IDriverRegistry.cs`

```csharp
using System.Collections.Generic;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Kernel.Registry
{
    /// <summary>
    /// Registry for managing driver instances.
    /// </summary>
    public interface IDriverRegistry
    {
        /// <summary>
        /// Registers a driver.
        /// </summary>
        void Register(IDriver driver, DriverRegistrationOptions options = null);

        /// <summary>
        /// Unregisters a driver by name.
        /// </summary>
        void Unregister(string driverName);

        /// <summary>
        /// Gets a driver by name.
        /// </summary>
        IDriver GetDriver(string name);

        /// <summary>
        /// Gets all registered drivers.
        /// </summary>
        IEnumerable<IDriver> GetAllDrivers();

        /// <summary>
        /// Gets all available drivers for the given context.
        /// </summary>
        IEnumerable<IDriver> GetAvailableDrivers(DriverContext context);

        /// <summary>
        /// Gets all drivers of a specific type.
        /// </summary>
        IEnumerable<IDriver> GetDriversByType(DriverType type);

        /// <summary>
        /// Gets all drivers for a specific runtime.
        /// </summary>
        IEnumerable<IDriver> GetDriversByRuntime(RuntimeType runtime);

        /// <summary>
        /// Gets the default driver for the given context.
        /// </summary>
        IDriver GetDefaultDriver(DriverContext context);

        /// <summary>
        /// Gets the registration options for a driver.
        /// </summary>
        DriverRegistrationOptions GetRegistrationOptions(string driverName);

        /// <summary>
        /// Checks if a driver is registered.
        /// </summary>
        bool IsRegistered(string driverName);
    }
}
```

**File**: `Ductus.FluentDocker/Kernel/Registry/DriverRegistry.cs`

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Kernel.Registry
{
    /// <summary>
    /// Default implementation of driver registry.
    /// </summary>
    public class DriverRegistry : IDriverRegistry
    {
        private readonly ConcurrentDictionary<string, IDriver> _drivers = new();
        private readonly ConcurrentDictionary<string, DriverRegistrationOptions> _options = new();

        public void Register(IDriver driver, DriverRegistrationOptions options = null)
        {
            if (driver == null)
                throw new ArgumentNullException(nameof(driver));

            options ??= new DriverRegistrationOptions();

            if (_drivers.TryAdd(driver.Name, driver))
            {
                _options[driver.Name] = options;
            }
            else
            {
                throw new InvalidOperationException($"Driver '{driver.Name}' is already registered");
            }
        }

        public void Unregister(string driverName)
        {
            if (_drivers.TryRemove(driverName, out var driver))
            {
                _options.TryRemove(driverName, out _);
                driver?.Dispose();
            }
        }

        public IDriver GetDriver(string name)
        {
            return _drivers.TryGetValue(name, out var driver) ? driver : null;
        }

        public IEnumerable<IDriver> GetAllDrivers()
        {
            return _drivers.Values;
        }

        public IEnumerable<IDriver> GetAvailableDrivers(DriverContext context)
        {
            return _drivers.Values.Where(d =>
            {
                var options = _options.GetValueOrDefault(d.Name);
                if (options?.AvailabilityChecker != null)
                {
                    return options.AvailabilityChecker(context);
                }
                return d.IsAvailable(context);
            });
        }

        public IEnumerable<IDriver> GetDriversByType(DriverType type)
        {
            return _drivers.Values.Where(d => d.Type == type);
        }

        public IEnumerable<IDriver> GetDriversByRuntime(RuntimeType runtime)
        {
            return _drivers.Values.Where(d => d.Runtime == runtime);
        }

        public IDriver GetDefaultDriver(DriverContext context)
        {
            // Get all available drivers sorted by priority
            var availableDrivers = GetAvailableDrivers(context)
                .Select(d => new
                {
                    Driver = d,
                    Options = _options.GetValueOrDefault(d.Name)
                })
                .OrderByDescending(x => x.Options?.Priority ?? 0)
                .ToList();

            // First, try default driver
            var defaultDriver = availableDrivers.FirstOrDefault(x => x.Options?.IsDefault == true);
            if (defaultDriver != null)
                return defaultDriver.Driver;

            // Otherwise, return highest priority driver
            return availableDrivers.FirstOrDefault()?.Driver;
        }

        public DriverRegistrationOptions GetRegistrationOptions(string driverName)
        {
            return _options.GetValueOrDefault(driverName);
        }

        public bool IsRegistered(string driverName)
        {
            return _drivers.ContainsKey(driverName);
        }
    }
}
```

### Task 2.2: Create Driver Selector

**File**: `Ductus.FluentDocker/Kernel/Selection/DriverSelectionCriteria.cs`

```csharp
using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Kernel.Selection
{
    /// <summary>
    /// Criteria for selecting a driver.
    /// </summary>
    public class DriverSelectionCriteria
    {
        /// <summary>
        /// Required container runtime type.
        /// </summary>
        public RuntimeType? RequiredRuntime { get; set; }

        /// <summary>
        /// Preferred driver type (CLI, API, etc.).
        /// </summary>
        public DriverType? PreferredType { get; set; }

        /// <summary>
        /// Set of required capabilities.
        /// </summary>
        public ISet<string> RequiredCapabilities { get; set; } = new HashSet<string>();

        /// <summary>
        /// Custom scoring function to rank drivers. Higher score = better match.
        /// </summary>
        public Func<IDriver, int> ScoringFunction { get; set; }

        /// <summary>
        /// Specific driver names to consider (in order of preference).
        /// </summary>
        public IList<string> PreferredDriverNames { get; set; } = new List<string>();
    }
}
```

**File**: `Ductus.FluentDocker/Kernel/Selection/IDriverSelector.cs`

```csharp
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Kernel.Selection
{
    /// <summary>
    /// Selects the most appropriate driver for a given context.
    /// </summary>
    public interface IDriverSelector
    {
        /// <summary>
        /// Selects the best driver for the given context and criteria.
        /// </summary>
        IDriver SelectDriver(DriverContext context, DriverSelectionCriteria criteria = null);
    }
}
```

**File**: `Ductus.FluentDocker/Kernel/Selection/DefaultDriverSelector.cs`

```csharp
using System;
using System.Linq;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Kernel.Registry;

namespace Ductus.FluentDocker.Kernel.Selection
{
    /// <summary>
    /// Default driver selection implementation.
    /// </summary>
    public class DefaultDriverSelector : IDriverSelector
    {
        private readonly IDriverRegistry _registry;

        public DefaultDriverSelector(IDriverRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public IDriver SelectDriver(DriverContext context, DriverSelectionCriteria criteria = null)
        {
            context ??= new DriverContext();
            criteria ??= new DriverSelectionCriteria();

            // Start with available drivers
            var candidates = _registry.GetAvailableDrivers(context).ToList();

            if (!candidates.Any())
            {
                throw new InvalidOperationException("No available drivers found");
            }

            // Apply context preferences
            var preferences = context.Preferences ?? new DriverPreferences();

            // Filter by runtime if specified
            if (preferences.TargetRuntime != RuntimeType.Auto)
            {
                candidates = candidates.Where(d => d.Runtime == preferences.TargetRuntime).ToList();
            }
            else if (criteria.RequiredRuntime.HasValue && criteria.RequiredRuntime.Value != RuntimeType.Auto)
            {
                candidates = candidates.Where(d => d.Runtime == criteria.RequiredRuntime.Value).ToList();
            }

            // Filter by required capabilities
            if (criteria.RequiredCapabilities?.Any() == true)
            {
                candidates = candidates.Where(d =>
                {
                    var caps = d.GetCapabilities();
                    return criteria.RequiredCapabilities.All(req => caps.HasCapability(req));
                }).ToList();
            }

            if (!candidates.Any())
            {
                throw new InvalidOperationException("No drivers match the specified criteria");
            }

            // Try preferred driver names first (from preferences)
            if (preferences.PreferredDrivers?.Any() == true)
            {
                foreach (var name in preferences.PreferredDrivers)
                {
                    var driver = candidates.FirstOrDefault(d => d.Name == name);
                    if (driver != null)
                        return driver;
                }
            }

            // Try preferred driver names from criteria
            if (criteria.PreferredDriverNames?.Any() == true)
            {
                foreach (var name in criteria.PreferredDriverNames)
                {
                    var driver = candidates.FirstOrDefault(d => d.Name == name);
                    if (driver != null)
                        return driver;
                }
            }

            // Apply custom scoring function if provided
            if (criteria.ScoringFunction != null)
            {
                return candidates.OrderByDescending(criteria.ScoringFunction).First();
            }

            // Score by driver type preference
            if (preferences.PreferredType != PreferredDriverType.Auto)
            {
                candidates = candidates.OrderByDescending(d => ScoreByPreferredType(d, preferences.PreferredType)).ToList();
            }

            // Score by registration priority
            var scored = candidates.Select(d => new
            {
                Driver = d,
                Priority = _registry.GetRegistrationOptions(d.Name)?.Priority ?? 0
            }).OrderByDescending(x => x.Priority);

            return scored.First().Driver;
        }

        private int ScoreByPreferredType(IDriver driver, PreferredDriverType preferredType)
        {
            return (preferredType, driver.Type) switch
            {
                (PreferredDriverType.API, DriverType.API) => 100,
                (PreferredDriverType.CLI, DriverType.CLI) => 100,
                (PreferredDriverType.Hybrid, DriverType.Hybrid) => 100,
                (PreferredDriverType.API, DriverType.Hybrid) => 50,
                (PreferredDriverType.CLI, DriverType.Hybrid) => 50,
                _ => 0
            };
        }
    }
}
```

### Task 2.3: Create FluentDockerKernel

**File**: `Ductus.FluentDocker/Kernel/FluentDockerKernel.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Docker.Cli;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Kernel.Registry;
using Ductus.FluentDocker.Kernel.Selection;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Images;
using Ductus.FluentDocker.Model.Networks;
using Ductus.FluentDocker.Model.Volumes;

namespace Ductus.FluentDocker.Kernel
{
    /// <summary>
    /// The FluentDocker kernel - central coordination layer for driver operations.
    /// </summary>
    public sealed class FluentDockerKernel : IDisposable
    {
        private static readonly Lazy<FluentDockerKernel> _instance =
            new Lazy<FluentDockerKernel>(() => new FluentDockerKernel());

        /// <summary>
        /// Gets the singleton kernel instance.
        /// </summary>
        public static FluentDockerKernel Instance => _instance.Value;

        private readonly IDriverRegistry _registry;
        private readonly IDriverSelector _selector;

        /// <summary>
        /// Gets the driver registry.
        /// </summary>
        public IDriverRegistry Registry => _registry;

        /// <summary>
        /// Gets the driver selector.
        /// </summary>
        public IDriverSelector Selector => _selector;

        private FluentDockerKernel()
        {
            _registry = new DriverRegistry();
            _selector = new DefaultDriverSelector(_registry);

            // Auto-register built-in drivers
            AutoRegisterDrivers();
        }

        /// <summary>
        /// Auto-registers available drivers.
        /// </summary>
        private void AutoRegisterDrivers()
        {
            // Try to register Docker CLI driver (existing implementation)
            try
            {
                var resolver = new DockerBinariesResolver(SudoMechanism.None, null);
                if (resolver.IsDockerAvailable)
                {
                    var dockerCliDriver = new DockerCliDriver();
                    _registry.Register(dockerCliDriver, new DriverRegistrationOptions
                    {
                        Priority = 100,  // Medium priority
                        IsDefault = true,
                        AutoInitialize = true
                    });
                }
            }
            catch
            {
                // Docker CLI driver not available
            }

            // TODO: Add Docker API driver registration when implemented
            // TODO: Add Podman CLI driver registration when implemented
        }

        /// <summary>
        /// Creates a container using the appropriate driver.
        /// </summary>
        public CommandResponse<string> CreateContainer(ContainerCreateParams createParams, DriverContext context = null)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Containers.Create(context, createParams);
        }

        /// <summary>
        /// Starts a container using the appropriate driver.
        /// </summary>
        public CommandResponse<string> StartContainer(string containerId, DriverContext context = null)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Containers.Start(context, containerId);
        }

        /// <summary>
        /// Stops a container using the appropriate driver.
        /// </summary>
        public CommandResponse<string> StopContainer(string containerId, DriverContext context = null, int? waitMs = null)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Containers.Stop(context, containerId, waitMs);
        }

        /// <summary>
        /// Removes a container using the appropriate driver.
        /// </summary>
        public CommandResponse<string> RemoveContainer(string containerId, DriverContext context = null, bool force = false, bool removeVolumes = false)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Containers.Remove(context, containerId, force, removeVolumes);
        }

        /// <summary>
        /// Inspects a container using the appropriate driver.
        /// </summary>
        public CommandResponse<Container> InspectContainer(string containerId, DriverContext context = null)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Containers.Inspect(context, containerId);
        }

        /// <summary>
        /// Lists containers using the appropriate driver.
        /// </summary>
        public CommandResponse<IList<Container>> ListContainers(DriverContext context = null, bool all = false, params string[] filters)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Containers.List(context, all, filters);
        }

        /// <summary>
        /// Pulls an image using the appropriate driver.
        /// </summary>
        public CommandResponse<string> PullImage(string image, DriverContext context = null, string authBase64 = null)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Images.Pull(context, image, authBase64);
        }

        /// <summary>
        /// Builds an image using the appropriate driver.
        /// </summary>
        public CommandResponse<IList<string>> BuildImage(string imageName, string workingDirectory, DriverContext context = null, string dockerFile = null, params string[] args)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Images.Build(context, imageName, workingDirectory, dockerFile, args);
        }

        /// <summary>
        /// Lists images using the appropriate driver.
        /// </summary>
        public CommandResponse<IList<DockerImageRowResponse>> ListImages(DriverContext context = null, bool all = false, params string[] filters)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Images.List(context, all, filters);
        }

        /// <summary>
        /// Creates a network using the appropriate driver.
        /// </summary>
        public CommandResponse<string> CreateNetwork(NetworkCreateParams createParams, DriverContext context = null)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Networks.Create(context, createParams);
        }

        /// <summary>
        /// Lists networks using the appropriate driver.
        /// </summary>
        public CommandResponse<IList<NetworkRow>> ListNetworks(DriverContext context = null, params string[] filters)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Networks.List(context, filters);
        }

        /// <summary>
        /// Creates a volume using the appropriate driver.
        /// </summary>
        public CommandResponse<Volume> CreateVolume(DriverContext context = null, string name = null, string driver = null, IDictionary<string, string> labels = null)
        {
            context ??= new DriverContext();
            var selectedDriver = _selector.SelectDriver(context);
            return selectedDriver.Volumes.Create(context, name, driver, labels);
        }

        /// <summary>
        /// Lists volumes using the appropriate driver.
        /// </summary>
        public CommandResponse<IList<Volume>> ListVolumes(DriverContext context = null, params string[] filters)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.Volumes.List(context, filters);
        }

        /// <summary>
        /// Gets system version using the appropriate driver.
        /// </summary>
        public CommandResponse<VersionResponse> GetVersion(DriverContext context = null)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context);
            return driver.System.Version(context);
        }

        public void Dispose()
        {
            foreach (var driver in _registry.GetAllDrivers())
            {
                driver?.Dispose();
            }
        }
    }
}
```

---

## Phase 3: Docker CLI Driver Implementation (Week 2)

### Task 3.1: Implement Base Driver

**File**: `Ductus.FluentDocker/Drivers/Docker/Cli/DockerCliDriver.cs`

```csharp
using System;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Drivers.Docker.Cli
{
    /// <summary>
    /// Docker CLI driver implementation.
    /// </summary>
    public class DockerCliDriver : IDriver
    {
        private readonly DockerBinariesResolver _resolver;
        private readonly Lazy<IContainerDriver> _containers;
        private readonly Lazy<IImageDriver> _images;
        private readonly Lazy<INetworkDriver> _networks;
        private readonly Lazy<IVolumeDriver> _volumes;
        private readonly Lazy<IComposeDriver> _compose;
        private readonly Lazy<ISystemDriver> _system;

        public string Name => "docker-cli";
        public string Version { get; private set; }
        public DriverType Type => DriverType.CLI;
        public RuntimeType Runtime => RuntimeType.Docker;

        public IContainerDriver Containers => _containers.Value;
        public IImageDriver Images => _images.Value;
        public INetworkDriver Networks => _networks.Value;
        public IVolumeDriver Volumes => _volumes.Value;
        public IComposeDriver Compose => _compose.Value;
        public ISystemDriver System => _system.Value;

        public DockerCliDriver()
        {
            _resolver = new DockerBinariesResolver(SudoMechanism.None, null);

            // Lazy initialization of driver components
            _containers = new Lazy<IContainerDriver>(() => new DockerCliContainerDriver(this));
            _images = new Lazy<IImageDriver>(() => new DockerCliImageDriver(this));
            _networks = new Lazy<INetworkDriver>(() => new DockerCliNetworkDriver(this));
            _volumes = new Lazy<IVolumeDriver>(() => new DockerCliVolumeDriver(this));
            _compose = new Lazy<IComposeDriver>(() => new DockerComposeCliDriver(this));
            _system = new Lazy<ISystemDriver>(() => new DockerCliSystemDriver(this));

            // Get Docker version
            try
            {
                var versionResponse = System.Version(new DriverContext());
                if (versionResponse.Success && versionResponse.Data != null)
                {
                    Version = versionResponse.Data.ServerVersion;
                }
            }
            catch
            {
                Version = "unknown";
            }
        }

        public DriverCapabilities GetCapabilities()
        {
            return new DriverCapabilities
            {
                SupportsContainers = true,
                SupportsImages = true,
                SupportsNetworks = true,
                SupportsVolumes = true,
                SupportsCompose = _resolver.IsDockerComposeV2Available || _resolver.IsDockerComposeAvailable,
                SupportsSwarm = true,
                SupportsSecrets = true,
                SupportsConfigs = true,
                SupportsPlugins = true,
                SupportsStacks = true,
                SupportsServices = true,
                SupportsHealthCheck = true,
                SupportsExec = true,
                SupportsAttach = true,
                SupportsLogs = true,
                SupportsStats = true,
                SupportsTop = true,
                SupportsCopy = true,
                SupportsDiff = true,
                SupportsBuild = true,
                SupportsMultiPlatformBuild = true,
                SupportsBuildx = true,
                SupportsBuildCache = true,
                SupportsImageImportExport = true,
                SupportsContentTrust = true,
                SupportsStreaming = false,  // CLI doesn't support true streaming
                SupportsBulkOperations = false,
                SupportsAsyncOperations = false
            };
        }

        public bool IsAvailable(DriverContext context)
        {
            return _resolver.IsDockerAvailable;
        }

        public DriverHealthStatus HealthCheck(DriverContext context)
        {
            try
            {
                var start = DateTime.UtcNow;
                var versionResponse = System.Version(context);
                var elapsed = DateTime.UtcNow - start;

                if (versionResponse.Success && versionResponse.Data != null)
                {
                    return new DriverHealthStatus
                    {
                        IsHealthy = true,
                        Message = "Docker CLI is healthy",
                        RuntimeVersion = versionResponse.Data.ServerVersion,
                        ApiVersion = versionResponse.Data.ApiVersion,
                        CheckedAt = DateTime.UtcNow,
                        ResponseTime = elapsed
                    };
                }

                return DriverHealthStatus.Unhealthy("Failed to get Docker version");
            }
            catch (Exception ex)
            {
                return DriverHealthStatus.Unhealthy("Docker CLI health check failed", ex);
            }
        }

        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}
```

### Task 3.2: Migrate Container Operations

**File**: `Ductus.FluentDocker/Drivers/Docker/Cli/DockerCliContainerDriver.cs`

This file will migrate all container operations from `Commands/Client.cs`. The implementation will use the existing `ProcessExecutor` infrastructure but within the driver pattern.

```csharp
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Drivers.Docker.Cli
{
    /// <summary>
    /// Docker CLI container driver implementation.
    /// </summary>
    public class DockerCliContainerDriver : IContainerDriver
    {
        private readonly DockerCliDriver _driver;

        public DockerCliContainerDriver(DockerCliDriver driver)
        {
            _driver = driver;
        }

        public CommandResponse<string> Create(DriverContext context, ContainerCreateParams createParams)
        {
            // Delegate to existing Commands layer (for now, during migration)
            return context.Host.Create(createParams, context.Certificates);
        }

        public CommandResponse<string> Start(DriverContext context, string containerId)
        {
            return context.Host.Start(containerId, context.Certificates);
        }

        public CommandResponse<string> Stop(DriverContext context, string containerId, int? waitMs = null)
        {
            return context.Host.Stop(containerId, waitMs, context.Certificates);
        }

        public CommandResponse<string> Pause(DriverContext context, string containerId)
        {
            return context.Host.Pause(containerId, context.Certificates);
        }

        public CommandResponse<string> Unpause(DriverContext context, string containerId)
        {
            return context.Host.UnPause(containerId, context.Certificates);
        }

        public CommandResponse<string> Remove(DriverContext context, string containerId, bool force = false, bool removeVolumes = false)
        {
            return context.Host.RemoveContainer(containerId, force, removeVolumes, context.Certificates);
        }

        public CommandResponse<string> Restart(DriverContext context, string containerId, int? waitMs = null)
        {
            // TODO: Implement restart command
            throw new System.NotImplementedException();
        }

        public CommandResponse<string> Kill(DriverContext context, string containerId, string signal = "SIGKILL")
        {
            return context.Host.Kill(containerId, signal, context.Certificates);
        }

        public CommandResponse<Container> Inspect(DriverContext context, string containerId)
        {
            return context.Host.InspectContainer(containerId, context.Certificates);
        }

        public CommandResponse<IList<Container>> List(DriverContext context, bool all = false, params string[] filters)
        {
            var response = context.Host.Ps(all, filters, context.Certificates);
            if (!response.Success)
            {
                return new CommandResponse<IList<Container>>
                {
                    Success = false,
                    Error = response.Error
                };
            }

            // Convert to full Container objects by inspecting each
            var containers = response.Data
                .Select(c => context.Host.InspectContainer(c.Id, context.Certificates).Data)
                .Where(c => c != null)
                .ToList();

            return new CommandResponse<IList<Container>>
            {
                Success = true,
                Data = containers
            };
        }

        public CommandResponse<Processes> Top(DriverContext context, string containerId, string psArgs = null)
        {
            return context.Host.Top(containerId, psArgs, context.Certificates);
        }

        public CommandResponse<IList<Diff>> Diff(DriverContext context, string containerId)
        {
            return context.Host.Diff(containerId, context.Certificates);
        }

        public CommandResponse<string> Logs(DriverContext context, string containerId, bool follow = false, bool timestamps = false)
        {
            // TODO: Implement logs command with options
            throw new System.NotImplementedException();
        }

        public CommandResponse<string> Execute(DriverContext context, string containerId, string command, params string[] args)
        {
            return context.Host.Execute(containerId, command, context.Certificates, args);
        }

        public CommandResponse<string> CopyTo(DriverContext context, string containerId, string hostPath, string containerPath)
        {
            return context.Host.CopyToContainer(containerId, hostPath, containerPath, context.Certificates);
        }

        public CommandResponse<string> CopyFrom(DriverContext context, string containerId, string containerPath, string hostPath)
        {
            return context.Host.CopyFromContainer(containerId, containerPath, hostPath, context.Certificates);
        }

        public CommandResponse<string> Rename(DriverContext context, string containerId, string newName)
        {
            // TODO: Implement rename command
            throw new System.NotImplementedException();
        }
    }
}
```

**Similar structure for**:
- `DockerCliImageDriver.cs` (migrate from `Commands/Images.cs`)
- `DockerCliNetworkDriver.cs` (migrate from `Commands/Network.cs`)
- `DockerCliVolumeDriver.cs` (migrate from `Commands/Volumes.cs`)
- `DockerComposeCliDriver.cs` (migrate from `Commands/Compose.cs`)
- `DockerCliSystemDriver.cs` (migrate from `Commands/Info.cs`)

---

## Implementation Checklist

### Phase 1: Core Infrastructure (Week 1) - COMPLETED
- [ ] Create `DriverType`, `RuntimeType`, `PreferredDriverType` enums
- [ ] Create `DriverContext` class
- [ ] Create `DriverPreferences` class
- [ ] Create `DriverCapabilities` class
- [ ] Create `DriverHealthStatus` class
- [ ] Create `IDriver` interface
- [ ] Create `IContainerDriver` interface
- [ ] Create `IImageDriver` interface
- [ ] Create `INetworkDriver` interface
- [ ] Create `IVolumeDriver` interface
- [ ] Create `IComposeDriver` interface
- [ ] Create `ISystemDriver` interface

### Phase 2: Kernel Infrastructure (Week 1) - COMPLETED
- [ ] Create `DriverRegistrationOptions` class
- [ ] Create `IDriverRegistry` interface
- [ ] Implement `DriverRegistry` class
- [ ] Create `DriverSelectionCriteria` class
- [ ] Create `IDriverSelector` interface
- [ ] Implement `DefaultDriverSelector` class
- [ ] Implement `FluentDockerKernel` class
- [ ] Add auto-registration logic

### Phase 3: Docker CLI Driver (Week 2) - IN PROGRESS
- [ ] Implement `DockerCliDriver` base class
- [ ] Implement `DockerCliContainerDriver`
- [ ] Implement `DockerCliImageDriver`
- [ ] Implement `DockerCliNetworkDriver`
- [ ] Implement `DockerCliVolumeDriver`
- [ ] Implement `DockerComposeCliDriver`
- [ ] Implement `DockerCliSystemDriver`
- [ ] Add unit tests for each driver component

### Phase 4: Commands Layer Migration (Week 2)
- [ ] Update `Commands/Client.cs` to delegate to kernel
- [ ] Update `Commands/Network.cs` to delegate to kernel
- [ ] Update `Commands/Images.cs` to delegate to kernel
- [ ] Update `Commands/Volumes.cs` to delegate to kernel
- [ ] Update `Commands/Compose.cs` to delegate to kernel
- [ ] Update `Commands/Info.cs` to delegate to kernel
- [ ] Ensure backward compatibility

### Phase 5: Services Layer Migration (Week 3)
- [ ] Update `DockerHostService` to use kernel
- [ ] Update `DockerContainerService` to use kernel
- [ ] Update `DockerNetworkService` to use kernel
- [ ] Update `DockerVolumeService` to use kernel
- [ ] Update `DockerComposeCompositeService` to use kernel
- [ ] Add integration tests

### Phase 6: Testing (Week 3)
- [ ] Create driver test suite base class
- [ ] Add unit tests for kernel
- [ ] Add unit tests for registry
- [ ] Add unit tests for selector
- [ ] Add integration tests for Docker CLI driver
- [ ] Add backward compatibility tests

---

## Next Steps

After completing the kernel and Docker CLI driver implementation, the next phases will be:

1. **Docker API Driver** (Weeks 4-5)
2. **Podman CLI Driver** (Weeks 6-7)
3. **Extension Methods for Unique Features** (Week 8)
4. **Performance Optimization** (Week 9)
5. **Documentation** (Week 10)

This implementation plan provides a solid foundation for the driver layer architecture while maintaining full backward compatibility with existing FluentDocker code.
