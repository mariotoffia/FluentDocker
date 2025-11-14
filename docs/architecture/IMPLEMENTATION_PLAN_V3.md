# FluentDocker v3.0.0 - Implementation Plan

## Overview

This document provides a detailed implementation plan for FluentDocker v3.0.0 with the new driver layer architecture.

**Key Changes from v2.x.x**:
- No singleton kernel - instantiable `FluentDockerKernel`
- Driver registration with unique IDs
- SysCtl() interface for driver access
- Fluent API binds to kernel instances
- Multiple driver instances supported

---

## Project Structure

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
│   │   └── IPodDriver.cs
│   ├── Models/                       # Driver-specific models
│   │   ├── DriverContext.cs
│   │   ├── DriverCapabilities.cs
│   │   ├── DriverType.cs
│   │   ├── RuntimeType.cs
│   │   ├── DriverComponent.cs        # NEW: For SysCtl()
│   │   ├── PreferredDriverType.cs
│   │   └── ...
│   ├── Docker/                       # Docker drivers
│   │   ├── Cli/                      # Docker CLI driver
│   │   │   ├── DockerCliDriver.cs
│   │   │   ├── DockerCliContainerDriver.cs
│   │   │   └── ...
│   │   └── Api/                      # Docker API driver
│   │       ├── DockerApiDriver.cs
│   │       └── ...
│   ├── Podman/                       # Podman drivers
│   │   └── Cli/
│   │       ├── PodmanCliDriver.cs
│   │       └── ...
│   └── Exceptions/
│       ├── DriverNotFoundException.cs
│       └── DriverException.cs
├── Kernel/                           # NEW: FluentDocker Kernel
│   ├── FluentDockerKernel.cs         # Main kernel (NOT singleton)
│   ├── IFluentDockerKernel.cs
│   ├── FluentDocker.cs               # Static default kernel helper
│   ├── FluentDockerKernelOptions.cs
│   ├── Registry/
│   │   ├── IDriverRegistry.cs        # UPDATED: ID-based
│   │   ├── DriverRegistry.cs
│   │   └── DriverRegistrationOptions.cs
│   ├── Selection/
│   │   ├── IDriverSelector.cs
│   │   ├── DefaultDriverSelector.cs
│   │   └── DriverSelectionCriteria.cs
│   └── Routing/
│       └── DriverRouter.cs
├── Builders/                         # UPDATED: Bind to kernel
│   ├── Builder.cs                    # BREAKING: Requires kernel
│   ├── ContainerBuilder.cs           # UPDATED: UseDriver() method
│   ├── ImageBuilder.cs
│   └── ...
├── Services/                         # UPDATED: Reference kernel
│   ├── IService.cs
│   ├── IContainerService.cs          # UPDATED: Kernel property
│   └── Impl/
│       ├── DockerContainerService.cs # UPDATED: Uses kernel
│       └── ...
└── Commands/                         # OPTIONAL: Keep as facade
    └── ...
```

---

## Phase 1: Core Infrastructure (Days 1-3)

### Task 1.1: Create Enums and Constants

**File**: `Drivers/Models/DriverComponent.cs`

```csharp
namespace Ductus.FluentDocker.Drivers.Models
{
    /// <summary>
    /// Driver component types for SysCtl() access.
    /// </summary>
    public enum DriverComponent
    {
        Container,
        Image,
        Network,
        Volume,
        Compose,
        System,
        Pod  // For Podman
    }
}
```

**File**: `Drivers/Models/DriverType.cs`

```csharp
namespace Ductus.FluentDocker.Drivers.Models
{
    public enum DriverType
    {
        CLI,
        API,
        Hybrid,
        Mock
    }
}
```

**File**: `Drivers/Models/RuntimeType.cs`

```csharp
namespace Ductus.FluentDocker.Drivers.Models
{
    public enum RuntimeType
    {
        Auto,
        Docker,
        Podman,
        Containerd,
        CriO
    }
}
```

**File**: `Drivers/Models/PreferredDriverType.cs`

```csharp
namespace Ductus.FluentDocker.Drivers.Models
{
    public enum PreferredDriverType
    {
        Auto,
        CLI,
        API,
        Hybrid
    }
}
```

### Task 1.2: Create Driver Exceptions

**File**: `Drivers/Exceptions/DriverNotFoundException.cs`

```csharp
using System;

namespace Ductus.FluentDocker.Drivers.Exceptions
{
    public class DriverNotFoundException : Exception
    {
        public string DriverId { get; }

        public DriverNotFoundException(string message) : base(message)
        {
        }

        public DriverNotFoundException(string message, string driverId)
            : base(message)
        {
            DriverId = driverId;
        }

        public DriverNotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
```

**File**: `Drivers/Exceptions/DriverException.cs`

```csharp
using System;

namespace Ductus.FluentDocker.Drivers.Exceptions
{
    public class DriverException : Exception
    {
        public string DriverId { get; }

        public DriverException(string message) : base(message)
        {
        }

        public DriverException(string message, string driverId)
            : base(message)
        {
            DriverId = driverId;
        }

        public DriverException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
```

### Task 1.3: Create Driver Context and Preferences

**File**: `Drivers/Models/DriverContext.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Drivers.Models
{
    public class DriverContext
    {
        public DockerUri Host { get; set; }
        public ICertificatePaths Certificates { get; set; }
        public SudoMechanism SudoMechanism { get; set; } = SudoMechanism.None;
        public TimeSpan? Timeout { get; set; }
        public DriverPreferences Preferences { get; set; }
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
        public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        public DriverContext()
        {
            Preferences = new DriverPreferences();
        }

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

**File**: `Drivers/Models/DriverPreferences.cs`

```csharp
using System.Collections.Generic;

namespace Ductus.FluentDocker.Drivers.Models
{
    public class DriverPreferences
    {
        /// <summary>
        /// Preferred driver ID to use (e.g., "docker-local", "podman-1").
        /// </summary>
        public string PreferredDriverId { get; set; }

        /// <summary>
        /// Ordered list of preferred driver IDs.
        /// </summary>
        public IList<string> PreferredDriverIds { get; set; } = new List<string>();

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
        /// Minimum priority for driver selection.
        /// </summary>
        public int MinimumPriority { get; set; } = 0;
    }
}
```

### Task 1.4: Create Driver Interfaces

**File**: `Drivers/Core/IDriver.cs`

```csharp
using System;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Drivers.Core
{
    public interface IDriver : IDisposable
    {
        /// <summary>
        /// Driver type name (e.g., "docker-cli", "docker-api", "podman-cli").
        /// This is the TYPE name, not the instance ID.
        /// </summary>
        string DriverTypeName { get; }

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
        /// Driver for system operations.
        /// </summary>
        ISystemDriver System { get; }

        /// <summary>
        /// Gets the capabilities supported by this driver.
        /// </summary>
        DriverCapabilities GetCapabilities();

        /// <summary>
        /// Checks if the driver is available.
        /// </summary>
        bool IsAvailable(DriverContext context);

        /// <summary>
        /// Performs a health check.
        /// </summary>
        DriverHealthStatus HealthCheck(DriverContext context);
    }
}
```

**File**: `Drivers/Core/IContainerDriver.cs`

```csharp
using System.Collections.Generic;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Drivers.Core
{
    public interface IContainerDriver
    {
        CommandResponse<string> Create(DriverContext context, ContainerCreateParams createParams);
        CommandResponse<string> Start(DriverContext context, string containerId);
        CommandResponse<string> Stop(DriverContext context, string containerId, int? waitMs = null);
        CommandResponse<string> Pause(DriverContext context, string containerId);
        CommandResponse<string> Unpause(DriverContext context, string containerId);
        CommandResponse<string> Remove(DriverContext context, string containerId, bool force = false, bool removeVolumes = false);
        CommandResponse<string> Restart(DriverContext context, string containerId, int? waitMs = null);
        CommandResponse<string> Kill(DriverContext context, string containerId, string signal = "SIGKILL");

        CommandResponse<Container> Inspect(DriverContext context, string containerId);
        CommandResponse<IList<Container>> List(DriverContext context, bool all = false, params string[] filters);
        CommandResponse<Processes> Top(DriverContext context, string containerId, string psArgs = null);
        CommandResponse<IList<Diff>> Diff(DriverContext context, string containerId);
        CommandResponse<string> Logs(DriverContext context, string containerId, bool follow = false, bool timestamps = false);

        CommandResponse<string> Execute(DriverContext context, string containerId, string command, params string[] args);
        CommandResponse<string> CopyTo(DriverContext context, string containerId, string hostPath, string containerPath);
        CommandResponse<string> CopyFrom(DriverContext context, string containerId, string containerPath, string hostPath);
        CommandResponse<string> Rename(DriverContext context, string containerId, string newName);
    }
}
```

Create similar interfaces for: `IImageDriver`, `INetworkDriver`, `IVolumeDriver`, `IComposeDriver`, `ISystemDriver`

---

## Phase 2: Kernel Infrastructure (Days 4-6)

### Task 2.1: Create Driver Registry (ID-based)

**File**: `Kernel/Registry/IDriverRegistry.cs`

```csharp
using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Kernel.Registry
{
    public interface IDriverRegistry : IDisposable
    {
        /// <summary>
        /// Registers a driver with a unique ID.
        /// </summary>
        void Register(string driverId, IDriver driver, DriverRegistrationOptions options = null);

        /// <summary>
        /// Unregisters a driver by ID.
        /// </summary>
        void Unregister(string driverId);

        /// <summary>
        /// Gets a driver by ID.
        /// </summary>
        IDriver GetDriver(string driverId);

        /// <summary>
        /// Gets all driver IDs.
        /// </summary>
        IEnumerable<string> GetAllDriverIds();

        /// <summary>
        /// Gets all drivers with their IDs.
        /// </summary>
        IEnumerable<KeyValuePair<string, IDriver>> GetAllDrivers();

        /// <summary>
        /// Gets registration options for a driver.
        /// </summary>
        DriverRegistrationOptions GetRegistrationOptions(string driverId);

        /// <summary>
        /// Checks if a driver is registered.
        /// </summary>
        bool IsRegistered(string driverId);

        /// <summary>
        /// Gets drivers by type.
        /// </summary>
        IEnumerable<KeyValuePair<string, IDriver>> GetDriversByType(DriverType type);

        /// <summary>
        /// Gets drivers by runtime.
        /// </summary>
        IEnumerable<KeyValuePair<string, IDriver>> GetDriversByRuntime(RuntimeType runtime);
    }
}
```

**File**: `Kernel/Registry/DriverRegistry.cs`

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Kernel.Registry
{
    public class DriverRegistry : IDriverRegistry
    {
        private readonly ConcurrentDictionary<string, IDriver> _drivers = new();
        private readonly ConcurrentDictionary<string, DriverRegistrationOptions> _options = new();

        public void Register(string driverId, IDriver driver, DriverRegistrationOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(driverId))
                throw new ArgumentException("Driver ID cannot be null or empty", nameof(driverId));

            if (driver == null)
                throw new ArgumentNullException(nameof(driver));

            options ??= new DriverRegistrationOptions();

            if (!_drivers.TryAdd(driverId, driver))
            {
                throw new InvalidOperationException($"Driver with ID '{driverId}' is already registered");
            }

            _options[driverId] = options;
        }

        public void Unregister(string driverId)
        {
            if (_drivers.TryRemove(driverId, out var driver))
            {
                _options.TryRemove(driverId, out _);
                driver?.Dispose();
            }
        }

        public IDriver GetDriver(string driverId)
        {
            return _drivers.TryGetValue(driverId, out var driver) ? driver : null;
        }

        public IEnumerable<string> GetAllDriverIds()
        {
            return _drivers.Keys;
        }

        public IEnumerable<KeyValuePair<string, IDriver>> GetAllDrivers()
        {
            return _drivers;
        }

        public DriverRegistrationOptions GetRegistrationOptions(string driverId)
        {
            return _options.TryGetValue(driverId, out var opts) ? opts : null;
        }

        public bool IsRegistered(string driverId)
        {
            return _drivers.ContainsKey(driverId);
        }

        public IEnumerable<KeyValuePair<string, IDriver>> GetDriversByType(DriverType type)
        {
            return _drivers.Where(kvp => kvp.Value.Type == type);
        }

        public IEnumerable<KeyValuePair<string, IDriver>> GetDriversByRuntime(RuntimeType runtime)
        {
            return _drivers.Where(kvp => kvp.Value.Runtime == runtime);
        }

        public void Dispose()
        {
            foreach (var driver in _drivers.Values)
            {
                driver?.Dispose();
            }
            _drivers.Clear();
            _options.Clear();
        }
    }
}
```

### Task 2.2: Create Driver Selector (Updated)

**File**: `Kernel/Selection/DefaultDriverSelector.cs`

```csharp
using System;
using System.Linq;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Exceptions;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Kernel.Registry;

namespace Ductus.FluentDocker.Kernel.Selection
{
    public class DefaultDriverSelector : IDriverSelector
    {
        public IDriver SelectDriver(DriverContext context, IDriverRegistry registry)
        {
            context ??= new DriverContext();
            var preferences = context.Preferences ?? new DriverPreferences();

            // 1. Try preferred driver ID
            if (!string.IsNullOrEmpty(preferences.PreferredDriverId))
            {
                var driver = registry.GetDriver(preferences.PreferredDriverId);
                if (driver != null && driver.IsAvailable(context))
                    return driver;

                if (!preferences.AllowFallback)
                    throw new DriverNotFoundException(
                        $"Preferred driver '{preferences.PreferredDriverId}' not found or unavailable",
                        preferences.PreferredDriverId);
            }

            // 2. Try ordered list of preferred IDs
            if (preferences.PreferredDriverIds?.Any() == true)
            {
                foreach (var driverId in preferences.PreferredDriverIds)
                {
                    var driver = registry.GetDriver(driverId);
                    if (driver != null && driver.IsAvailable(context))
                        return driver;
                }
            }

            // 3. Get all available drivers
            var candidates = registry.GetAllDrivers()
                .Where(kvp => kvp.Value.IsAvailable(context))
                .ToList();

            if (!candidates.Any())
                throw new DriverNotFoundException("No available drivers found");

            // 4. Filter by runtime if specified
            if (preferences.TargetRuntime != RuntimeType.Auto)
            {
                candidates = candidates
                    .Where(kvp => kvp.Value.Runtime == preferences.TargetRuntime)
                    .ToList();
            }

            if (!candidates.Any())
                throw new DriverNotFoundException(
                    $"No available drivers found for runtime {preferences.TargetRuntime}");

            // 5. Score and select best
            var scored = candidates.Select(kvp => new
            {
                DriverId = kvp.Key,
                Driver = kvp.Value,
                Score = ScoreDriver(kvp.Value, preferences, registry.GetRegistrationOptions(kvp.Key))
            })
            .OrderByDescending(x => x.Score)
            .ToList();

            return scored.First().Driver;
        }

        private int ScoreDriver(IDriver driver, DriverPreferences preferences,
            DriverRegistrationOptions options)
        {
            int score = options?.Priority ?? 100;

            // Bonus for matching preferred type
            if (preferences.PreferredType != PreferredDriverType.Auto)
            {
                score += (preferences.PreferredType, driver.Type) switch
                {
                    (PreferredDriverType.API, DriverType.API) => 50,
                    (PreferredDriverType.CLI, DriverType.CLI) => 50,
                    (PreferredDriverType.Hybrid, DriverType.Hybrid) => 50,
                    _ => 0
                };
            }

            // Bonus for default drivers
            if (options?.IsDefault == true)
                score += 25;

            return score;
        }
    }
}
```

### Task 2.3: Create FluentDockerKernel (Non-Singleton)

**File**: `Kernel/IFluentDockerKernel.cs`

```csharp
using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Kernel
{
    public interface IFluentDockerKernel : IDisposable
    {
        /// <summary>
        /// Access driver component via SysCtl interface.
        /// </summary>
        IDriverComponent SysCtl(string driverId, DriverComponent component);

        /// <summary>
        /// Type-safe SysCtl access.
        /// </summary>
        T SysCtl<T>(string driverId) where T : class;

        /// <summary>
        /// Get entire driver by ID.
        /// </summary>
        IDriver GetDriver(string driverId);

        /// <summary>
        /// Register a driver.
        /// </summary>
        void RegisterDriver(string driverId, IDriver driver, DriverRegistrationOptions options = null);

        /// <summary>
        /// Unregister a driver.
        /// </summary>
        void UnregisterDriver(string driverId);

        /// <summary>
        /// Get all registered driver IDs.
        /// </summary>
        IEnumerable<string> GetDriverIds();

        /// <summary>
        /// High-level container operations using driver selection.
        /// </summary>
        CommandResponse<string> CreateContainer(ContainerCreateParams createParams, DriverContext context = null);
        CommandResponse<string> StartContainer(string containerId, DriverContext context = null);
        CommandResponse<string> StopContainer(string containerId, DriverContext context = null, int? waitMs = null);
        // ... other operations
    }
}
```

**File**: `Kernel/FluentDockerKernel.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Docker.Cli;
using Ductus.FluentDocker.Drivers.Exceptions;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Kernel.Registry;
using Ductus.FluentDocker.Kernel.Selection;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Kernel
{
    public class FluentDockerKernel : IFluentDockerKernel
    {
        private readonly IDriverRegistry _registry;
        private readonly IDriverSelector _selector;

        public FluentDockerKernel(FluentDockerKernelOptions options = null)
        {
            options ??= new FluentDockerKernelOptions();

            _registry = new DriverRegistry();
            _selector = new DefaultDriverSelector();

            if (options.AutoRegisterDrivers)
            {
                AutoRegisterDrivers();
            }
        }

        // SysCtl() implementation
        public IDriverComponent SysCtl(string driverId, DriverComponent component)
        {
            var driver = _registry.GetDriver(driverId);
            if (driver == null)
                throw new DriverNotFoundException($"Driver '{driverId}' not found", driverId);

            return component switch
            {
                DriverComponent.Container => driver.Containers,
                DriverComponent.Image => driver.Images,
                DriverComponent.Network => driver.Networks,
                DriverComponent.Volume => driver.Volumes,
                DriverComponent.Compose => driver.Compose,
                DriverComponent.System => driver.System,
                _ => throw new ArgumentException($"Unknown component: {component}")
            };
        }

        // Type-safe SysCtl
        public T SysCtl<T>(string driverId) where T : class
        {
            var driver = _registry.GetDriver(driverId);
            if (driver == null)
                throw new DriverNotFoundException($"Driver '{driverId}' not found", driverId);

            if (typeof(T) == typeof(IContainerDriver)) return driver.Containers as T;
            if (typeof(T) == typeof(IImageDriver)) return driver.Images as T;
            if (typeof(T) == typeof(INetworkDriver)) return driver.Networks as T;
            if (typeof(T) == typeof(IVolumeDriver)) return driver.Volumes as T;
            if (typeof(T) == typeof(IComposeDriver)) return driver.Compose as T;
            if (typeof(T) == typeof(ISystemDriver)) return driver.System as T;

            throw new ArgumentException($"Unknown driver component type: {typeof(T).Name}");
        }

        public IDriver GetDriver(string driverId)
        {
            return _registry.GetDriver(driverId);
        }

        public void RegisterDriver(string driverId, IDriver driver, DriverRegistrationOptions options = null)
        {
            _registry.Register(driverId, driver, options);
        }

        public void UnregisterDriver(string driverId)
        {
            _registry.Unregister(driverId);
        }

        public IEnumerable<string> GetDriverIds()
        {
            return _registry.GetAllDriverIds();
        }

        private void AutoRegisterDrivers()
        {
            try
            {
                var resolver = new DockerBinariesResolver(SudoMechanism.None, null);
                if (resolver.IsDockerAvailable)
                {
                    var dockerCliDriver = new DockerCliDriver();
                    _registry.Register("docker-cli", dockerCliDriver, new DriverRegistrationOptions
                    {
                        Priority = 100,
                        IsDefault = true
                    });
                }
            }
            catch
            {
                // Docker not available
            }
        }

        // High-level operations
        public CommandResponse<string> CreateContainer(ContainerCreateParams createParams, DriverContext context = null)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context, _registry);
            return driver.Containers.Create(context, createParams);
        }

        public CommandResponse<string> StartContainer(string containerId, DriverContext context = null)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context, _registry);
            return driver.Containers.Start(context, containerId);
        }

        public CommandResponse<string> StopContainer(string containerId, DriverContext context = null, int? waitMs = null)
        {
            context ??= new DriverContext();
            var driver = _selector.SelectDriver(context, _registry);
            return driver.Containers.Stop(context, containerId, waitMs);
        }

        public void Dispose()
        {
            _registry?.Dispose();
        }
    }
}
```

### Task 2.4: Create Static Default Kernel Helper

**File**: `Kernel/FluentDocker.cs`

```csharp
using System;

namespace Ductus.FluentDocker.Kernel
{
    /// <summary>
    /// Static helper providing default kernel instance for convenience.
    /// </summary>
    public static class FluentDocker
    {
        private static Lazy<FluentDockerKernel> _defaultKernel =
            new Lazy<FluentDockerKernel>(() => new FluentDockerKernel());

        /// <summary>
        /// Gets the default kernel instance.
        /// </summary>
        public static FluentDockerKernel DefaultKernel => _defaultKernel.Value;

        /// <summary>
        /// Resets the default kernel (useful for testing).
        /// </summary>
        public static void ResetDefaultKernel()
        {
            if (_defaultKernel.IsValueCreated)
            {
                _defaultKernel.Value.Dispose();
            }
            _defaultKernel = new Lazy<FluentDockerKernel>(() => new FluentDockerKernel());
        }
    }
}
```

---

## Phase 3: Update Builders (Days 7-9)

### Task 3.1: Update Builder Base Class

**File**: `Builders/Builder.cs`

```csharp
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
    public class Builder : BaseBuilder<ICompositeService>
    {
        private readonly FluentDockerKernel _kernel;

        /// <summary>
        /// Creates a builder with explicit kernel.
        /// </summary>
        public Builder(FluentDockerKernel kernel)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        }

        /// <summary>
        /// Creates a builder using default kernel.
        /// </summary>
        public Builder() : this(FluentDocker.DefaultKernel)
        {
        }

        public FluentDockerKernel Kernel => _kernel;

        public HostBuilder UseHost()
        {
            return new HostBuilder(this, _kernel);
        }

        public ContainerBuilder UseContainer()
        {
            return new ContainerBuilder(this, _kernel);
        }

        public NetworkBuilder UseNetwork(string name = null)
        {
            return new NetworkBuilder(this, _kernel, name);
        }

        public VolumeBuilder UseVolume(string name = null)
        {
            return new VolumeBuilder(this, _kernel, name);
        }

        public override ICompositeService Build()
        {
            return new BuilderCompositeService(Children.Select(x => x.Build()).ToArray());
        }
    }
}
```

### Task 3.2: Update ContainerBuilder

**File**: `Builders/ContainerBuilder.cs`

```csharp
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Impl;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Builders
{
    public class ContainerBuilder : BaseBuilder<IContainerService>
    {
        private readonly FluentDockerKernel _kernel;
        private readonly ContainerBuilderConfig _config;
        private string _driverId;  // NEW: Specific driver ID

        public ContainerBuilder(IBuilder parent, FluentDockerKernel kernel)
            : base(parent)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _config = new ContainerBuilderConfig();
        }

        /// <summary>
        /// NEW: Specify which driver to use for this container.
        /// </summary>
        public ContainerBuilder UseDriver(string driverId)
        {
            _driverId = driverId;
            return this;
        }

        public ContainerBuilder UseImage(string image)
        {
            _config.Image = image;
            return this;
        }

        public ContainerBuilder WithName(string name)
        {
            _config.ContainerName = name;
            return this;
        }

        // ... all other builder methods

        public override IContainerService Build()
        {
            // Create context with driver preference
            var context = new DriverContext();
            if (!string.IsNullOrEmpty(_driverId))
            {
                context.Preferences.PreferredDriverId = _driverId;
            }

            // Create container using kernel
            var createParams = _config.ToCreateParams();
            var response = _kernel.CreateContainer(createParams, context);

            if (!response.Success)
                throw new FluentDockerException($"Failed to create container: {response.Error}");

            // Return service bound to kernel
            return new DockerContainerService(
                id: response.Data,
                image: _config.Image,
                kernel: _kernel,
                context: context
            );
        }
    }
}
```

---

## Phase 4: Update Services (Days 10-12)

### Task 4.1: Update IContainerService

**File**: `Services/IContainerService.cs`

```csharp
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services
{
    public interface IContainerService : IService
    {
        string Id { get; }
        string InstanceId { get; }

        // NEW: Kernel and driver info
        FluentDockerKernel Kernel { get; }
        string DriverId { get; }
        DriverContext Context { get; }

        // OLD properties moved to Context
        // DockerUri DockerHost { get; }  -> Context.Host
        // ICertificatePaths Certificates { get; }  -> Context.Certificates

        bool StopOnDispose { get; set; }
        bool RemoveOnDispose { get; set; }

        IContainerImageService Image { get; }
        Container GetConfiguration(bool fresh = false);
        IList<IVolumeService> GetVolumes();
        IList<INetworkService> GetNetworks();
    }
}
```

### Task 4.2: Update DockerContainerService

**File**: `Services/Impl/DockerContainerService.cs`

```csharp
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services.Impl
{
    public class DockerContainerService : ServiceBase, IContainerService
    {
        private readonly FluentDockerKernel _kernel;
        private readonly DriverContext _context;
        private readonly string _driverId;

        public string Id { get; private set; }
        public FluentDockerKernel Kernel => _kernel;
        public string DriverId => _driverId;
        public DriverContext Context => _context;

        public DockerContainerService(
            string id,
            string image,
            FluentDockerKernel kernel,
            DriverContext context)
        {
            Id = id;
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _context = context ?? new DriverContext();

            // Determine which driver was used
            _driverId = context.Preferences?.PreferredDriverId
                ?? kernel.GetDriverIds().FirstOrDefault();

            if (string.IsNullOrEmpty(_driverId))
                throw new InvalidOperationException("No driver available in kernel");
        }

        public override void Start()
        {
            if (State == ServiceRunningState.Running)
                return;

            var containerDriver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var response = containerDriver.Start(_context, Id);

            if (!response.Success)
                throw new FluentDockerException($"Failed to start container: {response.Error}");

            State = ServiceRunningState.Running;
            OnStateChange(ServiceRunningState.Running);
        }

        public override void Stop()
        {
            if (State == ServiceRunningState.Stopped)
                return;

            var containerDriver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var response = containerDriver.Stop(_context, Id);

            if (!response.Success)
                throw new FluentDockerException($"Failed to stop container: {response.Error}");

            State = ServiceRunningState.Stopped;
            OnStateChange(ServiceRunningState.Stopped);
        }

        public override void Remove(bool force = false)
        {
            var containerDriver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var response = containerDriver.Remove(_context, Id, force);

            if (!response.Success)
                throw new FluentDockerException($"Failed to remove container: {response.Error}");

            State = ServiceRunningState.Removed;
            OnStateChange(ServiceRunningState.Removed);
        }

        public Container GetConfiguration(bool fresh = false)
        {
            var containerDriver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var response = containerDriver.Inspect(_context, Id);

            if (!response.Success)
                throw new FluentDockerException($"Failed to inspect container: {response.Error}");

            return response.Data;
        }
    }
}
```

---

## Phase 5: Implement Docker CLI Driver (Days 13-17)

### Task 5.1: Implement DockerCliDriver

**File**: `Drivers/Docker/Cli/DockerCliDriver.cs`

```csharp
using System;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Drivers.Core;
using Ductus.FluentDocker.Drivers.Models;

namespace Ductus.FluentDocker.Drivers.Docker.Cli
{
    public class DockerCliDriver : IDriver
    {
        private readonly DockerBinariesResolver _resolver;
        private readonly DockerUri _host;
        private readonly ICertificatePaths _certificates;

        public string DriverTypeName => "docker-cli";
        public string Version { get; private set; }
        public DriverType Type => DriverType.CLI;
        public RuntimeType Runtime => RuntimeType.Docker;

        public IContainerDriver Containers { get; }
        public IImageDriver Images { get; }
        public INetworkDriver Networks { get; }
        public IVolumeDriver Volumes { get; }
        public IComposeDriver Compose { get; }
        public ISystemDriver System { get; }

        public DockerCliDriver(DockerUri host = null, ICertificatePaths certificates = null)
        {
            _host = host;
            _certificates = certificates;
            _resolver = new DockerBinariesResolver(SudoMechanism.None, null);

            Containers = new DockerCliContainerDriver(this, _host, _certificates);
            Images = new DockerCliImageDriver(this, _host, _certificates);
            Networks = new DockerCliNetworkDriver(this, _host, _certificates);
            Volumes = new DockerCliVolumeDriver(this, _host, _certificates);
            Compose = new DockerComposeCliDriver(this, _host, _certificates);
            System = new DockerCliSystemDriver(this, _host, _certificates);

            // Get version
            try
            {
                var context = new DriverContext { Host = _host, Certificates = _certificates };
                var versionResponse = System.Version(context);
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
                // ... all Docker features
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

### Task 5.2: Implement DockerCliContainerDriver

Migrate all container operations from `Commands/Client.cs` to this driver.

---

## Implementation Checklist

### Phase 1: Core Infrastructure (Days 1-3)
- [ ] Create enums (DriverType, RuntimeType, DriverComponent, etc.)
- [ ] Create exceptions (DriverNotFoundException, DriverException)
- [ ] Create DriverContext and DriverPreferences
- [ ] Create DriverCapabilities and DriverHealthStatus
- [ ] Create all driver interfaces (IDriver, IContainerDriver, etc.)
- [ ] Unit tests for models

### Phase 2: Kernel Infrastructure (Days 4-6)
- [ ] Implement DriverRegistry (ID-based)
- [ ] Implement DefaultDriverSelector
- [ ] Implement FluentDockerKernel (non-singleton)
- [ ] Implement SysCtl() methods
- [ ] Create FluentDocker static helper
- [ ] Unit tests for kernel and registry

### Phase 3: Update Builders (Days 7-9)
- [ ] Update Builder to accept kernel parameter
- [ ] Update ContainerBuilder with UseDriver() method
- [ ] Update ImageBuilder
- [ ] Update NetworkBuilder
- [ ] Update VolumeBuilder
- [ ] Update ComposeServiceBuilder
- [ ] Integration tests

### Phase 4: Update Services (Days 10-12)
- [ ] Update IContainerService interface
- [ ] Update DockerContainerService implementation
- [ ] Update IHostService interface
- [ ] Update DockerHostService implementation
- [ ] Update other services (Network, Volume, etc.)
- [ ] Integration tests

### Phase 5: Implement Docker CLI Driver (Days 13-17)
- [ ] Implement DockerCliDriver
- [ ] Implement DockerCliContainerDriver
- [ ] Implement DockerCliImageDriver
- [ ] Implement DockerCliNetworkDriver
- [ ] Implement DockerCliVolumeDriver
- [ ] Implement DockerComposeCliDriver
- [ ] Implement DockerCliSystemDriver
- [ ] Unit and integration tests

### Phase 6: Testing (Days 18-20)
- [ ] End-to-end tests
- [ ] Multiple kernel instance tests
- [ ] Multiple driver instance tests
- [ ] SysCtl() interface tests
- [ ] Driver selection tests
- [ ] Performance tests

---

## Total Timeline

**Estimated Duration**: 20 working days (~4 weeks)

- Phase 1: 3 days
- Phase 2: 3 days
- Phase 3: 3 days
- Phase 4: 3 days
- Phase 5: 5 days
- Phase 6: 3 days

After this, additional drivers can be implemented:
- Docker API Driver: ~1 week
- Podman CLI Driver: ~1 week
