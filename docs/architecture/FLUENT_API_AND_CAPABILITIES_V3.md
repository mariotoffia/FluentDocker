# FluentDocker v3.0.0 - Fluent API and Enhanced Capabilities

## Overview

This document describes the fluent API for driver registration and kernel configuration, along with the enhanced capability system with composable interfaces based on comprehensive Docker and Podman feature analysis.

**Key Enhancements:**
1. Fluent API for driver registration and kernel configuration
2. Composable driver interfaces (break down into smaller, focused interfaces)
3. Granular capability discovery system
4. Feature flags for fine-grained capability detection
5. Migration from static Fd.XXX methods

---

## Fluent API for Driver Registration

### Kernel Configuration Fluent API

```csharp
// Fluent kernel configuration - Build() is TERMINAL
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker-local", d => d
        .UseDockerCli()
        .AtHost("unix:///var/run/docker.sock"))
    .WithDriver("docker-remote", d => d
        .UseDockerApi()
        .AtHost("tcp://remote:2376")
        .WithCertificates("/path/to/certs")
        .WithTimeout(TimeSpan.FromSeconds(30))
        .AsPriority(200)
        .AsDefault())
    .WithDriver("podman", d => d
        .UsePodmanCli()
        .AsRootless()
        .WithPodSupport())
    .WithRetryPolicy(p => p
        .MaxAttempts(3)
        .InitialDelay(TimeSpan.FromSeconds(1))
        .ExponentialBackoff(2.0))
    .WithLogging(log => log
        .UseStructuredLogging()
        .MinimumLevel(LogLevel.Information))
    .WithMetrics<MyCustomMetrics>()
    .Build();  // TERMINAL - returns FluentDockerKernel
```

### Driver Builder Pattern (Lambda Configuration)

**Key principle: All builders configured via lambda, Build() is terminal**

```csharp
namespace FluentDocker.Kernel.Builders
{
    public interface IKernelBuilder
    {
        // Configure driver via lambda
        IKernelBuilder WithDriver(string driverId, Action<IDriverBuilder> configure);
        IKernelBuilder WithRetryPolicy(Action<IRetryPolicyBuilder> configure);
        IKernelBuilder WithLogging(Action<ILoggingBuilder> configure);
        IKernelBuilder WithMetrics<T>() where T : IFluentDockerMetrics, new();

        // TERMINAL - returns FluentDockerKernel
        FluentDockerKernel Build();
    }

    // Driver builder - not returned directly, only used in lambda
    public interface IDriverBuilder
    {
        IDockerCliDriverBuilder UseDockerCli();
        IDockerApiDriverBuilder UseDockerApi();
        IPodmanCliDriverBuilder UsePodmanCli();
    }

    // Docker CLI driver configuration
    public interface IDockerCliDriverBuilder
    {
        IDockerCliDriverBuilder AtHost(string hostUri);
        IDockerCliDriverBuilder WithCertificates(string certPath);
        IDockerCliDriverBuilder WithTimeout(TimeSpan timeout);
        IDockerCliDriverBuilder WithSudo(SudoMechanism mechanism);
        IDockerCliDriverBuilder AsPriority(int priority);
        IDockerCliDriverBuilder AsDefault();
        IDockerCliDriverBuilder WithComposeV2();
        IDockerCliDriverBuilder WithBuildx();
        // No Build() - configuration happens in lambda
    }

    // Docker API driver configuration
    public interface IDockerApiDriverBuilder
    {
        IDockerApiDriverBuilder AtHost(string hostUri);
        IDockerApiDriverBuilder WithCertificates(string certPath);
        IDockerApiDriverBuilder WithTimeout(TimeSpan timeout);
        IDockerApiDriverBuilder AsPriority(int priority);
        IDockerApiDriverBuilder AsDefault();
        IDockerApiDriverBuilder WithStreaming();
        IDockerApiDriverBuilder WithBulkOperations();
        // No Build() - configuration happens in lambda
    }

    // Podman CLI driver configuration
    public interface IPodmanCliDriverBuilder
    {
        IPodmanCliDriverBuilder AtHost(string hostUri);
        IPodmanCliDriverBuilder AsRootless();
        IPodmanCliDriverBuilder WithPodSupport();
        IPodmanCliDriverBuilder WithKubernetesYaml();
        IPodmanCliDriverBuilder WithSystemdIntegration();
        IPodmanCliDriverBuilder WithReducedCapabilities();
        IPodmanCliDriverBuilder AsPriority(int priority);
        // No Build() - configuration happens in lambda
    }

    // Retry policy configuration
    public interface IRetryPolicyBuilder
    {
        IRetryPolicyBuilder MaxAttempts(int attempts);
        IRetryPolicyBuilder InitialDelay(TimeSpan delay);
        IRetryPolicyBuilder ExponentialBackoff(double multiplier);
        IRetryPolicyBuilder MaxDelay(TimeSpan maxDelay);
        // No Build() - configuration happens in lambda
    }
}
```

### Implementation

```csharp
public class KernelBuilder : IKernelBuilder
{
    private readonly List<Action<FluentDockerKernel>> _configurations = new();
    private readonly FluentDockerKernelOptions _options = new();

    public static IKernelBuilder Create()
    {
        return new KernelBuilder();
    }

    public IDriverBuilder<IKernelBuilder> WithDriver(string driverId)
    {
        return new DriverBuilder<IKernelBuilder>(this, driverId, _configurations);
    }

    public IKernelBuilder WithRetryPolicy()
    {
        // Configure default retry policy
        return this;
    }

    public IKernelBuilder WithLogging(Action<ILoggingBuilder> configure)
    {
        var loggingBuilder = new LoggingBuilder();
        configure(loggingBuilder);
        _configurations.Add(kernel => loggingBuilder.Apply(kernel));
        return this;
    }

    public IKernelBuilder WithMetrics<T>() where T : IFluentDockerMetrics, new()
    {
        _configurations.Add(kernel => MetricsFactory.SetMetrics(new T()));
        return this;
    }

    public FluentDockerKernel Build()
    {
        var kernel = new FluentDockerKernel(_options);
        foreach (var config in _configurations)
        {
            config(kernel);
        }
        return kernel;
    }
}

public class DockerCliDriverBuilder<TReturn> : IDockerCliDriverBuilder<TReturn>
{
    private readonly TReturn _parent;
    private readonly string _driverId;
    private readonly List<Action<FluentDockerKernel>> _configurations;
    private string _hostUri = "unix:///var/run/docker.sock";
    private string _certPath;
    private TimeSpan? _timeout;
    private SudoMechanism _sudo = SudoMechanism.None;
    private int _priority = 100;
    private bool _isDefault;
    private bool _composeV2;
    private bool _buildx;

    public IDockerCliDriverBuilder<TReturn> AtHost(string hostUri)
    {
        _hostUri = hostUri;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> WithCertificates(string certPath)
    {
        _certPath = certPath;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> AsPriority(int priority)
    {
        _priority = priority;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> AsDefault()
    {
        _isDefault = true;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> WithComposeV2()
    {
        _composeV2 = true;
        return this;
    }

    public IDockerCliDriverBuilder<TReturn> WithBuildx()
    {
        _buildx = true;
        return this;
    }

    public TReturn Build()
    {
        _configurations.Add(kernel =>
        {
            var host = new DockerUri(_hostUri);
            var certs = !string.IsNullOrEmpty(_certPath) ? new CertificatePaths(_certPath) : null;

            var driver = new DockerCliDriver(host, certs)
            {
                Timeout = _timeout,
                SudoMechanism = _sudo,
                UseComposeV2 = _composeV2,
                UseBuildx = _buildx
            };

            kernel.RegisterDriver(_driverId, driver, new DriverRegistrationOptions
            {
                Priority = _priority,
                IsDefault = _isDefault
            });
        });

        return _parent;
    }
}
```

### Usage Examples

**Simple local Docker:**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker")
        .UseDockerCli()
        .Build()
    .Build();
```

**Multiple hosts with priorities:**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker-local")
        .UseDockerCli()
        .AsPriority(100)
        .Build()
    .WithDriver("docker-staging")
        .UseDockerApi()
        .AtHost("tcp://staging:2376")
        .WithCertificates("/certs/staging")
        .AsPriority(200)
        .Build()
    .WithDriver("docker-prod")
        .UseDockerApi()
        .AtHost("tcp://prod:2376")
        .WithCertificates("/certs/prod")
        .AsPriority(300)
        .AsDefault()
        .Build()
    .Build();
```

**Docker + Podman:**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker")
        .UseDockerCli()
        .WithComposeV2()
        .WithBuildx()
        .Build()
    .WithDriver("podman")
        .UsePodmanCli()
        .AsRootless()
        .WithPodSupport()
        .WithKubernetesYaml()
        .Build()
    .WithLogging(log => log
        .UseStructuredLogging()
        .MinimumLevel(LogLevel.Information))
    .Build();
```

---

## Scoped Fluent API Pattern with WithinDriver()

### Overview

The FluentDocker v3.0.0 Builder uses a **scoped driver pattern** where operations are grouped by driver and kernel context using `WithinDriver()`. This allows switching between different drivers and kernels within a single fluent chain.

**Key Concepts:**
- **No kernel in constructor**: `new Builder()` never takes a kernel parameter
- **Scope establishment**: `WithinDriver(driverId, optionalKernel)` establishes the active scope
- **Kernel reuse**: If kernel is omitted, the last kernel is reused
- **Automatic grouping**: Operations are automatically tracked and grouped by scope
- **Scope persistence**: The scope remains active until the next `WithinDriver()` call
- **Multi-scope chains**: Build multiple resources across different drivers/kernels in one chain

### Basic Scoped Builder Pattern

**Key Principle: Build() is Terminal**
- The Builder accumulates operations across scopes
- Build() executes all operations and returns BuildResults
- No need for separate GetResults() call

```csharp
namespace FluentDocker.Builders
{
    public class Builder : IFluentBuilder
    {
        private FluentDockerKernel _currentKernel;
        private string _currentDriverId;
        private readonly List<BuildOperation> _operations = new();

        // Establish or switch driver/kernel scope
        public Builder WithinDriver(string driverId, FluentDockerKernel kernel = null)
        {
            // Reuse last kernel if not specified
            _currentKernel = kernel ?? _currentKernel;

            if (_currentKernel == null)
                throw new InvalidOperationException("Kernel must be provided in first WithinDriver() call");

            _currentDriverId = driverId;
            return this;
        }

        // Configure and queue container creation
        public Builder UseContainer(Action<IContainerBuilder> configure)
        {
            ValidateScope();

            var builder = new ContainerBuilder(_currentKernel, _currentDriverId);
            configure(builder);

            // Queue the operation
            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel,
                DriverId = _currentDriverId,
                Execute = () => builder.Execute()
            });

            return this;
        }

        public Builder UseCompose(Action<IComposeBuilder> configure)
        {
            ValidateScope();

            var builder = new ComposeBuilder(_currentKernel, _currentDriverId);
            configure(builder);

            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel,
                DriverId = _currentDriverId,
                Execute = () => builder.Execute()
            });

            return this;
        }

        public Builder UseNetwork(Action<INetworkBuilder> configure)
        {
            ValidateScope();

            var builder = new NetworkBuilder(_currentKernel, _currentDriverId);
            configure(builder);

            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel,
                DriverId = _currentDriverId,
                Execute = () => builder.Execute()
            });

            return this;
        }

        public Builder UseVolume(Action<IVolumeBuilder> configure)
        {
            ValidateScope();

            var builder = new VolumeBuilder(_currentKernel, _currentDriverId);
            configure(builder);

            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel,
                DriverId = _currentDriverId,
                Execute = () => builder.Execute()
            });

            return this;
        }

        // TERMINAL: Build() executes all operations and returns results
        public BuildResults Build()
        {
            var scopes = new Dictionary<(FluentDockerKernel, string), BuildScope>();

            // Execute all operations and group by scope
            foreach (var operation in _operations)
            {
                var key = (operation.Kernel, operation.DriverId);

                if (!scopes.ContainsKey(key))
                {
                    scopes[key] = new BuildScope(operation.Kernel, operation.DriverId);
                }

                var service = operation.Execute();
                scopes[key].AddResult(service);
            }

            return new BuildResults(scopes.Values.ToList());
        }

        private void ValidateScope()
        {
            if (_currentKernel == null || _currentDriverId == null)
                throw new InvalidOperationException("Must call WithinDriver() before using builder operations");
        }
    }

    internal class BuildOperation
    {
        public FluentDockerKernel Kernel { get; set; }
        public string DriverId { get; set; }
        public Func<IService> Execute { get; set; }
    }
}
```

### Build Scope Tracking

```csharp
public class BuildScope
{
    public FluentDockerKernel Kernel { get; }
    public string DriverId { get; }
    public List<IService> Results { get; } = new();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public BuildScope(FluentDockerKernel kernel, string driverId)
    {
        Kernel = kernel;
        DriverId = driverId;
    }

    internal void AddResult(IService service)
    {
        Results.Add(service);
    }
}

public class BuildResults
{
    private readonly List<BuildScope> _scopes;

    public BuildResults(List<BuildScope> scopes)
    {
        _scopes = scopes;
    }

    // Get all services across all scopes
    public IReadOnlyList<IService> All => _scopes.SelectMany(s => s.Results).ToList();

    // Get services for specific driver
    public IReadOnlyList<IService> ForDriver(string driverId) =>
        _scopes.Where(s => s.DriverId == driverId)
                .SelectMany(s => s.Results)
                .ToList();

    // Get services for specific kernel
    public IReadOnlyList<IService> ForKernel(FluentDockerKernel kernel) =>
        _scopes.Where(s => s.Kernel == kernel)
                .SelectMany(s => s.Results)
                .ToList();

    // Get all scopes
    public IReadOnlyList<BuildScope> Scopes => _scopes;

    // Group by driver ID
    public Dictionary<string, List<IService>> GroupedByDriver =>
        _scopes.GroupBy(s => s.DriverId)
                .ToDictionary(g => g.Key, g => g.SelectMany(s => s.Results).ToList());

    // Dispose all services
    public void DisposeAll()
    {
        foreach (var service in All)
        {
            service?.Dispose();
        }
    }
}
```

### Container Builder (Configured via Lambda)

```csharp
public class ContainerBuilder : IContainerBuilder
{
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;
    private string _image;
    private string _name;
    private readonly List<string> _command = new();
    private readonly List<string> _exposedPorts = new();
    private readonly Dictionary<string, string> _env = new();
    // ... other configuration fields

    public ContainerBuilder(FluentDockerKernel kernel, string driverId)
    {
        _kernel = kernel;
        _driverId = driverId;
    }

    // Configuration methods return this for chaining
    public IContainerBuilder UseImage(string image)
    {
        _image = image;
        return this;
    }

    public IContainerBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public IContainerBuilder Command(params string[] command)
    {
        _command.AddRange(command);
        return this;
    }

    public IContainerBuilder ExposePort(int port, string protocol = "tcp")
    {
        _exposedPorts.Add($"{port}/{protocol}");
        return this;
    }

    public IContainerBuilder WithEnvironment(string key, string value)
    {
        _env[key] = value;
        return this;
    }

    // Internal execution method called by Builder.Build()
    internal IService Execute()
    {
        // Get container driver from kernel
        var containerDriver = _kernel.SysCtl<IContainerDriver>(_driverId);

        // Create container configuration
        var config = new ContainerCreateConfig
        {
            Image = _image,
            Name = _name,
            Command = _command.ToArray(),
            ExposedPorts = _exposedPorts.ToArray(),
            Env = _env.Select(kv => $"{kv.Key}={kv.Value}").ToArray(),
            // ... other config
        };

        var context = new DriverContext
        {
            DriverId = _driverId,
            OperationId = Guid.NewGuid().ToString()
        };

        // Execute container creation
        var response = containerDriver.Create(context, config);
        if (!response.Success)
        {
            throw new ContainerCreationException(
                $"Failed to create container: {response.Error}",
                response.ErrorCode,
                response.Context);
        }

        // Return service
        return new DockerContainerService(_kernel, _driverId, response.Data.Id);
    }
}
```

### Usage Examples

**Key principle: Build() is terminal - call it once at the end**

**Single scope (one driver/kernel):**
```csharp
// Kernel builder is also terminal
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker-local")
        .UseDockerCli()
    .Build();  // TERMINAL - returns FluentDockerKernel

// Builder.Build() is terminal - returns BuildResults
var results = new Builder()
    .WithinDriver("docker-local", kernel)
        .UseContainer(c => c
            .UseImage("nginx")
            .WithName("web"))
        .UseContainer(c => c
            .UseImage("redis")
            .WithName("cache"))
        .UseNetwork(n => n
            .WithName("my-network"))
    .Build();  // TERMINAL - executes all operations, returns BuildResults

// Access results
var nginx = results.All[0];
var redis = results.All[1];
var network = results.All[2];

// Or filter by driver
var services = results.ForDriver("docker-local");  // [nginx, redis, network]
```

**Multiple scopes (different drivers, same kernel):**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker-cli")
        .UseDockerCli()
        .Build()
    .WithDriver("docker-api")
        .UseDockerApi()
        .AtHost("tcp://remote:2376")
        .WithCertificates("/certs")
        .Build()
    .Build();

var results = new Builder()
    .WithinDriver("docker-cli", kernel)
        .UseContainer()
            .UseImage("nginx")
            .Build()
        .UseContainer()
            .UseImage("redis")
            .Build()
    .WithinDriver("docker-api")  // Reuses kernel from previous scope
        .UseContainer()
            .UseImage("postgres")
            .Build()
    .GetResults();

// results.ForDriver("docker-cli") => [nginx, redis]
// results.ForDriver("docker-api") => [postgres]
```

**Multiple scopes (different kernels and drivers):**
```csharp
var localKernel = FluentDockerKernel.Create()
    .WithDriver("docker-local")
        .UseDockerCli()
        .Build()
    .Build();

var remoteKernel = FluentDockerKernel.Create()
    .WithDriver("docker-remote")
        .UseDockerApi()
        .AtHost("tcp://remote:2376")
        .Build()
    .Build();

var podmanKernel = FluentDockerKernel.Create()
    .WithDriver("podman")
        .UsePodmanCli()
        .AsRootless()
        .Build()
    .Build();

var results = new Builder()
    .WithinDriver("docker-local", localKernel)
        .UseContainer()
            .UseImage("nginx")
            .WithName("local-nginx")
            .Build()
    .WithinDriver("docker-remote", remoteKernel)
        .UseContainer()
            .UseImage("postgres")
            .WithName("remote-postgres")
            .Build()
    .WithinDriver("podman", podmanKernel)
        .UseContainer()
            .UseImage("redis")
            .WithName("podman-redis")
            .Build()
    .GetResults();

// results.ForKernel(localKernel) => [local-nginx]
// results.ForKernel(remoteKernel) => [remote-postgres]
// results.ForKernel(podmanKernel) => [podman-redis]
// results.GroupedByDriver => { "docker-local": [...], "docker-remote": [...], "podman": [...] }
```

**Kernel reuse within scopes:**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker-1")
        .UseDockerCli()
        .AtHost("unix:///var/run/docker.sock")
        .Build()
    .WithDriver("docker-2")
        .UseDockerApi()
        .AtHost("tcp://remote:2376")
        .Build()
    .Build();

var results = new Builder()
    .WithinDriver("docker-1", kernel)  // Explicitly set kernel
        .UseContainer()
            .UseImage("nginx")
            .Build()
    .WithinDriver("docker-2")  // Reuses kernel from previous WithinDriver()
        .UseContainer()
            .UseImage("postgres")
            .Build()
    .WithinDriver("docker-1")  // Still reuses same kernel
        .UseContainer()
            .UseImage("redis")
            .Build()
    .GetResults();

// All operations use the same kernel instance
```

**Docker Compose with scoping:**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker")
        .UseDockerCli()
        .WithComposeV2()
        .Build()
    .Build();

var results = new Builder()
    .WithinDriver("docker", kernel)
        .UseCompose()
            .FromFile("docker-compose.yml")
            .Build()
        .UseContainer()  // Additional container in same scope
            .UseImage("monitoring/prometheus")
            .Build()
    .GetResults();

// results.ForDriver("docker") => [compose service, prometheus container]
```

**Advanced: Mixed operations across multiple hosts:**
```csharp
var dev = FluentDockerKernel.Create()
    .WithDriver("dev-docker").UseDockerCli().Build()
    .Build();

var staging = FluentDockerKernel.Create()
    .WithDriver("staging-docker").UseDockerApi().AtHost("tcp://staging:2376").Build()
    .Build();

var prod = FluentDockerKernel.Create()
    .WithDriver("prod-docker").UseDockerApi().AtHost("tcp://prod:2376").Build()
    .Build();

var deployment = new Builder()
    .WithinDriver("dev-docker", dev)
        .UseContainer()
            .UseImage("myapp:dev")
            .WithName("myapp-dev")
            .Build()
        .UseContainer()
            .UseImage("postgres:14")
            .WithName("db-dev")
            .Build()
    .WithinDriver("staging-docker", staging)
        .UseContainer()
            .UseImage("myapp:staging")
            .WithName("myapp-staging")
            .Build()
        .UseContainer()
            .UseImage("postgres:14")
            .WithName("db-staging")
            .Build()
    .WithinDriver("prod-docker", prod)
        .UseContainer()
            .UseImage("myapp:v1.2.3")
            .WithName("myapp-prod")
            .Build()
        .UseContainer()
            .UseImage("postgres:14")
            .WithName("db-prod")
            .Build()
    .GetResults();

// Deploy to all environments in single chain
Console.WriteLine($"Deployed {deployment.All.Count} containers across {deployment.Scopes.Count} environments");
Console.WriteLine($"Dev: {deployment.ForDriver("dev-docker").Count} containers");
Console.WriteLine($"Staging: {deployment.ForDriver("staging-docker").Count} containers");
Console.WriteLine($"Prod: {deployment.ForDriver("prod-docker").Count} containers");
```

### Resource Management

**Using statement for automatic cleanup:**
```csharp
using var deployment = new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer().UseImage("nginx").Build()
        .UseContainer().UseImage("redis").Build()
    .GetResults();

// All resources automatically disposed at end of using block
```

**Manual cleanup:**
```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer().UseImage("nginx").Build()
    .GetResults();

try
{
    // Use services
    var nginx = results.All[0];
    // ...
}
finally
{
    results.DisposeAll();  // Cleanup all services
}
```

**Selective cleanup:**
```csharp
var results = new Builder()
    .WithinDriver("docker-1", kernel)
        .UseContainer().UseImage("nginx").Build()
    .WithinDriver("docker-2", kernel)
        .UseContainer().UseImage("redis").Build()
    .GetResults();

// Cleanup only services from docker-1
foreach (var service in results.ForDriver("docker-1"))
{
    service.Dispose();
}

// Keep docker-2 services running
```

### Error Handling in Scoped Operations

```csharp
var results = new Builder();

try
{
    results = new Builder()
        .WithinDriver("docker", kernel)
            .UseContainer()
                .UseImage("nginx")
                .Build()
        .GetResults();
}
catch (DriverNotFoundException ex)
{
    Console.WriteLine($"Driver not found: {ex.Context.DriverId}");
}
catch (ContainerCreationException ex)
{
    Console.WriteLine($"Failed to create container: {ex.Message}");
    Console.WriteLine($"Error code: {ex.ErrorCode}");
    Console.WriteLine($"StdErr: {ex.Context.StdErr}");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("Must call WithinDriver"))
{
    Console.WriteLine("Forgot to call WithinDriver() before operations");
}
finally
{
    results?.DisposeAll();
}
```

### Benefits of Scoped Pattern

**1. Multi-environment deployment:**
Deploy to multiple Docker hosts/Podman instances in a single chain.

**2. Kernel isolation:**
Each scope can use different kernels with different configurations.

**3. Automatic tracking:**
All built resources are automatically tracked and grouped.

**4. Flexible switching:**
Switch between drivers/kernels at any point in the chain.

**5. Kernel reuse:**
Omit kernel parameter to reuse the last kernel.

**6. Clear scoping:**
Explicit `WithinDriver()` makes it clear which driver/kernel is being used.

**7. Simplified cleanup:**
GetResults() provides convenient methods for cleanup by scope.

**8. No constructor kernel:**
Builder is always instantiated the same way: `new Builder()`

---

## Composable Driver Interfaces

### Interface Breakdown Strategy

Break down large interfaces (IContainerDriver, INetworkDriver, etc.) into smaller, focused sub-interfaces that drivers can implement selectively.

### Container Operations - Composable Design

```csharp
namespace FluentDocker.Drivers.Core.Container
{
    /// <summary>
    /// Main container driver interface - aggregates all container sub-interfaces.
    /// </summary>
    public interface IContainerDriver :
        IContainerLifecycle,
        IContainerInspection,
        IContainerExecution,
        IContainerFiles,
        IContainerLogs,
        IContainerStats,
        IContainerProcesses,
        IContainerHealth
    {
    }

    /// <summary>
    /// Container lifecycle operations (create, start, stop, remove).
    /// </summary>
    public interface IContainerLifecycle
    {
        CommandResponse<string> Create(DriverContext context, ContainerCreateParams createParams);
        CommandResponse<string> Start(DriverContext context, string containerId);
        CommandResponse<string> Stop(DriverContext context, string containerId, int? waitMs = null);
        CommandResponse<string> Restart(DriverContext context, string containerId, int? waitMs = null);
        CommandResponse<string> Pause(DriverContext context, string containerId);
        CommandResponse<string> Unpause(DriverContext context, string containerId);
        CommandResponse<string> Kill(DriverContext context, string containerId, string signal = "SIGKILL");
        CommandResponse<string> Remove(DriverContext context, string containerId, bool force = false, bool removeVolumes = false);
        CommandResponse<string> Rename(DriverContext context, string containerId, string newName);
    }

    /// <summary>
    /// Container inspection and listing.
    /// </summary>
    public interface IContainerInspection
    {
        CommandResponse<Container> Inspect(DriverContext context, string containerId);
        CommandResponse<IList<Container>> List(DriverContext context, bool all = false, ContainerListFilter filter = null);
        CommandResponse<IList<Diff>> Diff(DriverContext context, string containerId);
        CommandResponse<bool> Exists(DriverContext context, string containerId);
        CommandResponse<ContainerState> GetState(DriverContext context, string containerId);
    }

    /// <summary>
    /// Command execution inside containers.
    /// </summary>
    public interface IContainerExecution
    {
        CommandResponse<ExecResult> Execute(DriverContext context, string containerId, ExecParams execParams);
        CommandResponse<string> Attach(DriverContext context, string containerId, AttachParams attachParams);
        CommandResponse<string> CreateExec(DriverContext context, string containerId, ExecCreateParams createParams);
        CommandResponse<ExecResult> StartExec(DriverContext context, string execId, bool detach = false);
        CommandResponse<ExecInfo> InspectExec(DriverContext context, string execId);
    }

    /// <summary>
    /// File operations (copy to/from container).
    /// </summary>
    public interface IContainerFiles
    {
        CommandResponse<string> CopyTo(DriverContext context, string containerId, string hostPath, string containerPath);
        CommandResponse<string> CopyFrom(DriverContext context, string containerId, string containerPath, string hostPath);
        CommandResponse<Stream> GetArchive(DriverContext context, string containerId, string path);
        CommandResponse<string> PutArchive(DriverContext context, string containerId, string path, Stream archive);
    }

    /// <summary>
    /// Container logs.
    /// </summary>
    public interface IContainerLogs
    {
        CommandResponse<string> GetLogs(DriverContext context, string containerId, LogOptions options = null);
        CommandResponse<Stream> StreamLogs(DriverContext context, string containerId, LogOptions options = null);
    }

    /// <summary>
    /// Container statistics.
    /// </summary>
    public interface IContainerStats
    {
        CommandResponse<ContainerStats> GetStats(DriverContext context, string containerId, bool stream = false);
        CommandResponse<Stream> StreamStats(DriverContext context, string containerId);
    }

    /// <summary>
    /// Container process information.
    /// </summary>
    public interface IContainerProcesses
    {
        CommandResponse<Processes> Top(DriverContext context, string containerId, string psArgs = null);
        CommandResponse<IList<Process>> ListProcesses(DriverContext context, string containerId);
    }

    /// <summary>
    /// Container health checks.
    /// </summary>
    public interface IContainerHealth
    {
        CommandResponse<HealthStatus> GetHealth(DriverContext context, string containerId);
        CommandResponse<bool> IsHealthy(DriverContext context, string containerId);
        CommandResponse<string> UpdateHealthCheck(DriverContext context, string containerId, HealthCheckConfig config);
    }
}
```

### Image Operations - Composable Design

```csharp
namespace FluentDocker.Drivers.Core.Image
{
    /// <summary>
    /// Main image driver interface - aggregates all image sub-interfaces.
    /// </summary>
    public interface IImageDriver :
        IImageLifecycle,
        IImageBuild,
        IImageRegistry,
        IImageInspection,
        IImageExport
    {
    }

    /// <summary>
    /// Image lifecycle operations.
    /// </summary>
    public interface IImageLifecycle
    {
        CommandResponse<string> Pull(DriverContext context, string image, string tag = "latest", AuthConfig auth = null);
        CommandResponse<string> Remove(DriverContext context, string imageId, bool force = false, bool noPrune = false);
        CommandResponse<string> Tag(DriverContext context, string sourceImage, string targetImage, string tag = "latest");
        CommandResponse<string> Prune(DriverContext context, ImagePruneFilter filter = null);
    }

    /// <summary>
    /// Image building.
    /// </summary>
    public interface IImageBuild
    {
        CommandResponse<IList<string>> Build(DriverContext context, ImageBuildParams buildParams);
        CommandResponse<IList<ImageHistory>> History(DriverContext context, string imageId);
    }

    /// <summary>
    /// Image build with advanced features (BuildX, multi-platform).
    /// </summary>
    public interface IImageBuildAdvanced : IImageBuild
    {
        CommandResponse<IList<string>> BuildMultiPlatform(DriverContext context, BuildXParams buildxParams);
        CommandResponse<string> CreateBuilder(DriverContext context, BuilderCreateParams createParams);
        CommandResponse<IList<Builder>> ListBuilders(DriverContext context);
        CommandResponse<string> InspectBuilder(DriverContext context, string builderName);
        CommandResponse<string> RemoveBuilder(DriverContext context, string builderName);
    }

    /// <summary>
    /// Registry operations (push, search).
    /// </summary>
    public interface IImageRegistry
    {
        CommandResponse<string> Push(DriverContext context, string image, string tag = "latest", AuthConfig auth = null);
        CommandResponse<IList<ImageSearchResult>> Search(DriverContext context, string term, int limit = 25);
    }

    /// <summary>
    /// Image inspection and listing.
    /// </summary>
    public interface IImageInspection
    {
        CommandResponse<ImageConfig> Inspect(DriverContext context, string imageId);
        CommandResponse<IList<DockerImageRowResponse>> List(DriverContext context, bool all = false, ImageListFilter filter = null);
        CommandResponse<bool> Exists(DriverContext context, string imageName);
    }

    /// <summary>
    /// Image import/export.
    /// </summary>
    public interface IImageExport
    {
        CommandResponse<string> Save(DriverContext context, string[] images, string outputPath);
        CommandResponse<string> Load(DriverContext context, string inputPath);
        CommandResponse<Stream> Export(DriverContext context, string[] images);
        CommandResponse<string> Import(DriverContext context, Stream tarStream, string repository, string tag);
    }
}
```

### Network Operations - Composable Design

```csharp
namespace FluentDocker.Drivers.Core.Network
{
    /// <summary>
    /// Main network driver interface - aggregates all network sub-interfaces.
    /// </summary>
    public interface INetworkDriver :
        INetworkLifecycle,
        INetworkConnectivity,
        INetworkInspection
    {
    }

    /// <summary>
    /// Network lifecycle operations.
    /// </summary>
    public interface INetworkLifecycle
    {
        CommandResponse<string> Create(DriverContext context, NetworkCreateParams createParams);
        CommandResponse<string> Remove(DriverContext context, string networkId);
        CommandResponse<string> Prune(DriverContext context, NetworkPruneFilter filter = null);
    }

    /// <summary>
    /// Network connectivity (connect/disconnect containers).
    /// </summary>
    public interface INetworkConnectivity
    {
        CommandResponse<string> Connect(DriverContext context, string networkId, string containerId, NetworkConnectParams connectParams = null);
        CommandResponse<string> Disconnect(DriverContext context, string networkId, string containerId, bool force = false);
        CommandResponse<IList<string>> ListConnectedContainers(DriverContext context, string networkId);
    }

    /// <summary>
    /// Network inspection and listing.
    /// </summary>
    public interface INetworkInspection
    {
        CommandResponse<NetworkConfiguration> Inspect(DriverContext context, string networkId);
        CommandResponse<IList<NetworkRow>> List(DriverContext context, NetworkListFilter filter = null);
        CommandResponse<bool> Exists(DriverContext context, string networkId);
    }
}
```

### Volume Operations - Composable Design

```csharp
namespace FluentDocker.Drivers.Core.Volume
{
    /// <summary>
    /// Main volume driver interface - aggregates all volume sub-interfaces.
    /// </summary>
    public interface IVolumeDriver :
        IVolumeLifecycle,
        IVolumeInspection
    {
    }

    /// <summary>
    /// Volume lifecycle operations.
    /// </summary>
    public interface IVolumeLifecycle
    {
        CommandResponse<Volume> Create(DriverContext context, VolumeCreateParams createParams);
        CommandResponse<string> Remove(DriverContext context, string volumeName, bool force = false);
        CommandResponse<string> Prune(DriverContext context, VolumePruneFilter filter = null);
    }

    /// <summary>
    /// Volume inspection and listing.
    /// </summary>
    public interface IVolumeInspection
    {
        CommandResponse<Volume> Inspect(DriverContext context, string volumeName);
        CommandResponse<IList<Volume>> List(DriverContext context, VolumeListFilter filter = null);
        CommandResponse<bool> Exists(DriverContext context, string volumeName);
    }
}
```

### Compose Operations - Composable Design

```csharp
namespace FluentDocker.Drivers.Core.Compose
{
    /// <summary>
    /// Main compose driver interface - aggregates all compose sub-interfaces.
    /// </summary>
    public interface IComposeDriver :
        IComposeLifecycle,
        IComposeOperations,
        IComposeInspection
    {
    }

    /// <summary>
    /// Compose project lifecycle.
    /// </summary>
    public interface IComposeLifecycle
    {
        CommandResponse<string> Up(DriverContext context, ComposeUpParams upParams);
        CommandResponse<string> Down(DriverContext context, ComposeDownParams downParams);
        CommandResponse<string> Start(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Stop(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Restart(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Pause(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Unpause(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Kill(DriverContext context, ComposeParams composeParams, string signal = "SIGKILL");
        CommandResponse<string> Remove(DriverContext context, ComposeRemoveParams removeParams);
    }

    /// <summary>
    /// Compose operations (build, pull, scale).
    /// </summary>
    public interface IComposeOperations
    {
        CommandResponse<string> Build(DriverContext context, ComposeBuildParams buildParams);
        CommandResponse<string> Pull(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Push(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Scale(DriverContext context, ComposeScaleParams scaleParams);
        CommandResponse<string> Exec(DriverContext context, ComposeExecParams execParams);
        CommandResponse<string> Run(DriverContext context, ComposeRunParams runParams);
    }

    /// <summary>
    /// Compose inspection.
    /// </summary>
    public interface IComposeInspection
    {
        CommandResponse<IList<ComposeContainer>> Ps(DriverContext context, ComposeParams composeParams);
        CommandResponse<DockerComposeConfig> Config(DriverContext context, ComposeParams composeParams);
        CommandResponse<string> Logs(DriverContext context, ComposeLogsParams logsParams);
        CommandResponse<PortMapping> Port(DriverContext context, ComposeParams composeParams, string service, int port);
        CommandResponse<string> Version(DriverContext context);
    }
}
```

### System Operations - Composable Design

```csharp
namespace FluentDocker.Drivers.Core.System
{
    /// <summary>
    /// Main system driver interface - aggregates all system sub-interfaces.
    /// </summary>
    public interface ISystemDriver :
        ISystemInfo,
        ISystemAuth,
        ISystemEvents,
        ISystemMaintenance
    {
    }

    /// <summary>
    /// System information and version.
    /// </summary>
    public interface ISystemInfo
    {
        CommandResponse<VersionResponse> Version(DriverContext context);
        CommandResponse<SystemInfo> Info(DriverContext context);
        CommandResponse<bool> IsWindowsEngine(DriverContext context);
        CommandResponse<string> Ping(DriverContext context);
    }

    /// <summary>
    /// Registry authentication.
    /// </summary>
    public interface ISystemAuth
    {
        CommandResponse<AuthResult> Login(DriverContext context, AuthConfig auth);
        CommandResponse<string> Logout(DriverContext context, string registry = null);
    }

    /// <summary>
    /// Docker events.
    /// </summary>
    public interface ISystemEvents
    {
        CommandResponse<FdEvent[]> GetEvents(DriverContext context, EventsParams eventsParams);
        CommandResponse<Stream> StreamEvents(DriverContext context, EventsParams eventsParams);
    }

    /// <summary>
    /// System maintenance (disk usage, pruning).
    /// </summary>
    public interface ISystemMaintenance
    {
        CommandResponse<DiskUsage> DiskUsage(DriverContext context);
        CommandResponse<PruneReport> Prune(DriverContext context, PruneOptions options = null);
    }
}
```

### Podman-Specific Interfaces

```csharp
namespace FluentDocker.Drivers.Core.Podman
{
    /// <summary>
    /// Podman pod operations (unique to Podman).
    /// </summary>
    public interface IPodDriver :
        IPodLifecycle,
        IPodInspection
    {
    }

    /// <summary>
    /// Pod lifecycle operations.
    /// </summary>
    public interface IPodLifecycle
    {
        CommandResponse<string> Create(DriverContext context, PodCreateParams createParams);
        CommandResponse<string> Start(DriverContext context, string podId);
        CommandResponse<string> Stop(DriverContext context, string podId);
        CommandResponse<string> Restart(DriverContext context, string podId);
        CommandResponse<string> Pause(DriverContext context, string podId);
        CommandResponse<string> Unpause(DriverContext context, string podId);
        CommandResponse<string> Kill(DriverContext context, string podId, string signal = "SIGKILL");
        CommandResponse<string> Remove(DriverContext context, string podId, bool force = false);
        CommandResponse<string> Prune(DriverContext context);
    }

    /// <summary>
    /// Pod inspection and listing.
    /// </summary>
    public interface IPodInspection
    {
        CommandResponse<Pod> Inspect(DriverContext context, string podId);
        CommandResponse<IList<Pod>> List(DriverContext context, PodListFilter filter = null);
        CommandResponse<bool> Exists(DriverContext context, string podId);
        CommandResponse<Processes> Top(DriverContext context, string podId, string psArgs = null);
        CommandResponse<ContainerStats> Stats(DriverContext context, string podId);
    }

    /// <summary>
    /// Kubernetes YAML generation and playback (unique to Podman).
    /// </summary>
    public interface IKubernetesYaml
    {
        CommandResponse<string> Generate(DriverContext context, KubeGenerateParams generateParams);
        CommandResponse<string> Play(DriverContext context, KubePlayParams playParams);
        CommandResponse<string> Down(DriverContext context, string yamlPath);
    }

    /// <summary>
    /// Systemd service generation (unique to Podman).
    /// </summary>
    public interface ISystemdGeneration
    {
        CommandResponse<string> Generate(DriverContext context, SystemdGenerateParams generateParams);
    }
}
```

---

## Enhanced Capability System

### DriverCapabilities Enhancement

```csharp
namespace FluentDocker.Drivers.Models
{
    /// <summary>
    /// Comprehensive driver capabilities based on Docker and Podman feature analysis.
    /// </summary>
    public class DriverCapabilities
    {
        // === Core Resource Types ===
        public bool SupportsContainers { get; set; }
        public bool SupportsImages { get; set; }
        public bool SupportsNetworks { get; set; }
        public bool SupportsVolumes { get; set; }

        // === Container Capabilities (Sub-interface support) ===
        public ContainerCapabilities Container { get; set; } = new();

        // === Image Capabilities (Sub-interface support) ===
        public ImageCapabilities Image { get; set; } = new();

        // === Network Capabilities (Sub-interface support) ===
        public NetworkCapabilities Network { get; set; } = new();

        // === Volume Capabilities (Sub-interface support) ===
        public VolumeCapabilities Volume { get; set; } = new();

        // === Compose Support ===
        public ComposeCapabilities Compose { get; set; } = new();

        // === Docker-Specific Features ===
        public DockerSpecificCapabilities DockerSpecific { get; set; } = new();

        // === Podman-Specific Features ===
        public PodmanSpecificCapabilities PodmanSpecific { get; set; } = new();

        // === Security Features ===
        public SecurityCapabilities Security { get; set; } = new();

        // === Performance Features ===
        public PerformanceCapabilities Performance { get; set; } = new();

        // === Runtime Information ===
        public string RuntimeType { get; set; }  // "docker", "podman", etc.
        public string RuntimeVersion { get; set; }
        public string ApiVersion { get; set; }

        /// <summary>
        /// Checks if driver implements a specific interface.
        /// </summary>
        public bool Implements<T>() where T : class
        {
            var interfaceType = typeof(T);

            // Container sub-interfaces
            if (interfaceType == typeof(IContainerLifecycle)) return Container.SupportsLifecycle;
            if (interfaceType == typeof(IContainerInspection)) return Container.SupportsInspection;
            if (interfaceType == typeof(IContainerExecution)) return Container.SupportsExecution;
            if (interfaceType == typeof(IContainerFiles)) return Container.SupportsFileOperations;
            if (interfaceType == typeof(IContainerLogs)) return Container.SupportsLogs;
            if (interfaceType == typeof(IContainerStats)) return Container.SupportsStats;
            if (interfaceType == typeof(IContainerProcesses)) return Container.SupportsProcessInfo;
            if (interfaceType == typeof(IContainerHealth)) return Container.SupportsHealthChecks;

            // Image sub-interfaces
            if (interfaceType == typeof(IImageLifecycle)) return Image.SupportsLifecycle;
            if (interfaceType == typeof(IImageBuild)) return Image.SupportsBuild;
            if (interfaceType == typeof(IImageBuildAdvanced)) return Image.SupportsBuildX;
            if (interfaceType == typeof(IImageRegistry)) return Image.SupportsRegistry;
            if (interfaceType == typeof(IImageInspection)) return Image.SupportsInspection;
            if (interfaceType == typeof(IImageExport)) return Image.SupportsImportExport;

            // Network sub-interfaces
            if (interfaceType == typeof(INetworkLifecycle)) return Network.SupportsLifecycle;
            if (interfaceType == typeof(INetworkConnectivity)) return Network.SupportsConnectivity;
            if (interfaceType == typeof(INetworkInspection)) return Network.SupportsInspection;

            // Volume sub-interfaces
            if (interfaceType == typeof(IVolumeLifecycle)) return Volume.SupportsLifecycle;
            if (interfaceType == typeof(IVolumeInspection)) return Volume.SupportsInspection;

            // Compose sub-interfaces
            if (interfaceType == typeof(IComposeLifecycle)) return Compose.SupportsLifecycle;
            if (interfaceType == typeof(IComposeOperations)) return Compose.SupportsOperations;
            if (interfaceType == typeof(IComposeInspection)) return Compose.SupportsInspection;

            // Podman-specific
            if (interfaceType == typeof(IPodDriver)) return PodmanSpecific.SupportsPods;
            if (interfaceType == typeof(IKubernetesYaml)) return PodmanSpecific.SupportsKubernetesYaml;
            if (interfaceType == typeof(ISystemdGeneration)) return PodmanSpecific.SupportsSystemdGeneration;

            return false;
        }

        /// <summary>
        /// Gets all implemented interfaces.
        /// </summary>
        public IEnumerable<Type> GetImplementedInterfaces()
        {
            var interfaces = new List<Type>();

            if (Container.SupportsLifecycle) interfaces.Add(typeof(IContainerLifecycle));
            if (Container.SupportsInspection) interfaces.Add(typeof(IContainerInspection));
            if (Container.SupportsExecution) interfaces.Add(typeof(IContainerExecution));
            if (Container.SupportsFileOperations) interfaces.Add(typeof(IContainerFiles));
            if (Container.SupportsLogs) interfaces.Add(typeof(IContainerLogs));
            if (Container.SupportsStats) interfaces.Add(typeof(IContainerStats));
            if (Container.SupportsProcessInfo) interfaces.Add(typeof(IContainerProcesses));
            if (Container.SupportsHealthChecks) interfaces.Add(typeof(IContainerHealth));

            // ... add all other interfaces

            return interfaces;
        }
    }

    /// <summary>
    /// Container-specific capabilities.
    /// </summary>
    public class ContainerCapabilities
    {
        // Sub-interface support
        public bool SupportsLifecycle { get; set; } = true;
        public bool SupportsInspection { get; set; } = true;
        public bool SupportsExecution { get; set; } = true;
        public bool SupportsFileOperations { get; set; } = true;
        public bool SupportsLogs { get; set; } = true;
        public bool SupportsStats { get; set; } = true;
        public bool SupportsProcessInfo { get; set; } = true;
        public bool SupportsHealthChecks { get; set; } = true;

        // Fine-grained capabilities
        public bool SupportsAttach { get; set; } = true;
        public bool SupportsExecCreate { get; set; } = true;
        public bool SupportsRename { get; set; } = true;
        public bool SupportsUpdate { get; set; } = true;
        public bool SupportsWait { get; set; } = true;
        public bool SupportsArchiveOperations { get; set; } = true;
        public bool SupportsStreamingLogs { get; set; } = false;
        public bool SupportsStreamingStats { get; set; } = false;
    }

    /// <summary>
    /// Image-specific capabilities.
    /// </summary>
    public class ImageCapabilities
    {
        // Sub-interface support
        public bool SupportsLifecycle { get; set; } = true;
        public bool SupportsBuild { get; set; } = true;
        public bool SupportsBuildX { get; set; } = false;
        public bool SupportsRegistry { get; set; } = true;
        public bool SupportsInspection { get; set; } = true;
        public bool SupportsImportExport { get; set; } = true;

        // Fine-grained capabilities
        public bool SupportsMultiPlatformBuild { get; set; } = false;
        public bool SupportsBuildCache { get; set; } = true;
        public bool SupportsImagePrune { get; set; } = true;
        public bool SupportsImageHistory { get; set; } = true;
        public bool SupportsImageSearch { get; set; } = true;
        public bool SupportsContentTrust { get; set; } = false;
    }

    /// <summary>
    /// Network-specific capabilities.
    /// </summary>
    public class NetworkCapabilities
    {
        // Sub-interface support
        public bool SupportsLifecycle { get; set; } = true;
        public bool SupportsConnectivity { get; set; } = true;
        public bool SupportsInspection { get; set; } = true;

        // Fine-grained capabilities
        public bool SupportsCustomDrivers { get; set; } = true;
        public bool SupportsIPAM { get; set; } = true;
        public bool SupportsIPv6 { get; set; } = true;
        public bool SupportsOverlay { get; set; } = true;
        public bool SupportsMacvlan { get; set; } = true;
        public bool SupportsNetworkPrune { get; set; } = true;
    }

    /// <summary>
    /// Volume-specific capabilities.
    /// </summary>
    public class VolumeCapabilities
    {
        // Sub-interface support
        public bool SupportsLifecycle { get; set; } = true;
        public bool SupportsInspection { get; set; } = true;

        // Fine-grained capabilities
        public bool SupportsCustomDrivers { get; set; } = true;
        public bool SupportsVolumePrune { get; set; } = true;
        public bool SupportsLabels { get; set; } = true;
    }

    /// <summary>
    /// Compose-specific capabilities.
    /// </summary>
    public class ComposeCapabilities
    {
        // Sub-interface support
        public bool SupportsLifecycle { get; set; } = false;
        public bool SupportsOperations { get; set; } = false;
        public bool SupportsInspection { get; set; } = false;

        // Version support
        public bool SupportsComposeV1 { get; set; } = false;
        public bool SupportsComposeV2 { get; set; } = false;

        // Fine-grained capabilities
        public bool SupportsProfiles { get; set; } = false;
        public bool SupportsScale { get; set; } = false;
        public bool SupportsConfigValidation { get; set; } = false;
    }

    /// <summary>
    /// Docker-specific capabilities.
    /// </summary>
    public class DockerSpecificCapabilities
    {
        public bool SupportsSwarm { get; set; } = false;
        public bool SupportsSecrets { get; set; } = false;
        public bool SupportsConfigs { get; set; } = false;
        public bool SupportsStacks { get; set; } = false;
        public bool SupportsServices { get; set; } = false;
        public bool SupportsPlugins { get; set; } = false;
        public bool SupportsContentTrust { get; set; } = false;
        public bool SupportsBuildCloud { get; set; } = false;
    }

    /// <summary>
    /// Podman-specific capabilities.
    /// </summary>
    public class PodmanSpecificCapabilities
    {
        public bool SupportsPods { get; set; } = false;
        public bool SupportsKubernetesYaml { get; set; } = false;
        public bool SupportsSystemdGeneration { get; set; } = false;
        public bool SupportsRootless { get; set; } = false;
        public bool SupportsReducedCapabilities { get; set; } = false;
    }

    /// <summary>
    /// Security-related capabilities.
    /// </summary>
    public class SecurityCapabilities
    {
        public bool SupportsRootless { get; set; } = false;
        public bool SupportsSELinux { get; set; } = false;
        public bool SupportsAppArmor { get; set; } = false;
        public bool SupportsSeccomp { get; set; } = false;
        public bool SupportsUserNamespaces { get; set; } = false;
        public int DefaultCapabilityCount { get; set; } = 14;  // Docker: 14, Podman: 11
    }

    /// <summary>
    /// Performance-related capabilities.
    /// </summary>
    public class PerformanceCapabilities
    {
        public bool SupportsStreaming { get; set; } = false;
        public bool SupportsBulkOperations { get; set; } = false;
        public bool SupportsAsyncOperations { get; set; } = false;
        public bool SupportsParallelPull { get; set; } = false;
        public bool SupportsBuildCache { get; set; } = false;
    }
}
```

### Docker CLI Driver Capabilities

```csharp
public class DockerCliDriver : IDriver
{
    public DriverCapabilities GetCapabilities()
    {
        return new DriverCapabilities
        {
            RuntimeType = "docker",
            RuntimeVersion = Version,

            // Container capabilities
            Container = new ContainerCapabilities
            {
                SupportsLifecycle = true,
                SupportsInspection = true,
                SupportsExecution = true,
                SupportsFileOperations = true,
                SupportsLogs = true,
                SupportsStats = true,
                SupportsProcessInfo = true,
                SupportsHealthChecks = true,
                SupportsStreamingLogs = false,  // CLI doesn't support true streaming
                SupportsStreamingStats = false
            },

            // Image capabilities
            Image = new ImageCapabilities
            {
                SupportsLifecycle = true,
                SupportsBuild = true,
                SupportsBuildX = _useBuildx,
                SupportsRegistry = true,
                SupportsInspection = true,
                SupportsImportExport = true,
                SupportsMultiPlatformBuild = _useBuildx,
                SupportsBuildCache = true,
                SupportsContentTrust = true
            },

            // Network capabilities
            Network = new NetworkCapabilities
            {
                SupportsLifecycle = true,
                SupportsConnectivity = true,
                SupportsInspection = true,
                SupportsCustomDrivers = true,
                SupportsIPAM = true,
                SupportsIPv6 = true,
                SupportsOverlay = true,
                SupportsMacvlan = true
            },

            // Volume capabilities
            Volume = new VolumeCapabilities
            {
                SupportsLifecycle = true,
                SupportsInspection = true,
                SupportsCustomDrivers = true,
                SupportsVolumePrune = true
            },

            // Compose capabilities
            Compose = new ComposeCapabilities
            {
                SupportsLifecycle = _supportsCompose,
                SupportsOperations = _supportsCompose,
                SupportsInspection = _supportsCompose,
                SupportsComposeV1 = _resolver.IsDockerComposeAvailable,
                SupportsComposeV2 = _resolver.IsDockerComposeV2Available,
                SupportsProfiles = _resolver.IsDockerComposeV2Available
            },

            // Docker-specific
            DockerSpecific = new DockerSpecificCapabilities
            {
                SupportsSwarm = true,
                SupportsSecrets = true,
                SupportsConfigs = true,
                SupportsStacks = true,
                SupportsServices = true,
                SupportsPlugins = true,
                SupportsContentTrust = true
            },

            // Security
            Security = new SecurityCapabilities
            {
                SupportsRootless = true,  // Docker supports rootless mode
                SupportsSELinux = true,
                SupportsAppArmor = true,
                SupportsSeccomp = true,
                SupportsUserNamespaces = true,
                DefaultCapabilityCount = 14
            },

            // Performance
            Performance = new PerformanceCapabilities
            {
                SupportsStreaming = false,  // CLI doesn't stream
                SupportsBulkOperations = false,
                SupportsAsyncOperations = false,
                SupportsBuildCache = true
            }
        };
    }
}
```

### Podman CLI Driver Capabilities

```csharp
public class PodmanCliDriver : IDriver
{
    public DriverCapabilities GetCapabilities()
    {
        return new DriverCapabilities
        {
            RuntimeType = "podman",
            RuntimeVersion = Version,

            // Container capabilities
            Container = new ContainerCapabilities
            {
                SupportsLifecycle = true,
                SupportsInspection = true,
                SupportsExecution = true,
                SupportsFileOperations = true,
                SupportsLogs = true,
                SupportsStats = true,
                SupportsProcessInfo = true,
                SupportsHealthChecks = true,
                SupportsStreamingLogs = false,
                SupportsStreamingStats = false
            },

            // Image capabilities
            Image = new ImageCapabilities
            {
                SupportsLifecycle = true,
                SupportsBuild = true,
                SupportsBuildX = false,  // Podman doesn't support BuildX
                SupportsRegistry = true,
                SupportsInspection = true,
                SupportsImportExport = true,
                SupportsMultiPlatformBuild = true,  // Podman supports multi-arch
                SupportsBuildCache = true
            },

            // Network capabilities
            Network = new NetworkCapabilities
            {
                SupportsLifecycle = true,
                SupportsConnectivity = true,
                SupportsInspection = true,
                SupportsCustomDrivers = true,
                SupportsIPAM = true,
                SupportsIPv6 = true,
                SupportsOverlay = false,  // Podman doesn't support overlay networks
                SupportsMacvlan = true
            },

            // Volume capabilities
            Volume = new VolumeCapabilities
            {
                SupportsLifecycle = true,
                SupportsInspection = true,
                SupportsCustomDrivers = true,
                SupportsVolumePrune = true
            },

            // Compose capabilities
            Compose = new ComposeCapabilities
            {
                SupportsLifecycle = _supportsPodmanCompose,
                SupportsOperations = _supportsPodmanCompose,
                SupportsInspection = _supportsPodmanCompose,
                SupportsComposeV1 = false,
                SupportsComposeV2 = _supportsPodmanCompose  // Via podman-compose
            },

            // Podman-specific
            PodmanSpecific = new PodmanSpecificCapabilities
            {
                SupportsPods = true,
                SupportsKubernetesYaml = true,
                SupportsSystemdGeneration = true,
                SupportsRootless = true,
                SupportsReducedCapabilities = true
            },

            // Security
            Security = new SecurityCapabilities
            {
                SupportsRootless = true,  // Rootless by default
                SupportsSELinux = true,
                SupportsAppArmor = true,
                SupportsSeccomp = true,
                SupportsUserNamespaces = true,
                DefaultCapabilityCount = 11  // Podman uses 11 vs Docker's 14
            },

            // Performance
            Performance = new PerformanceCapabilities
            {
                SupportsStreaming = false,
                SupportsBulkOperations = false,
                SupportsAsyncOperations = false,
                SupportsBuildCache = true
            }
        };
    }
}
```

### Usage: Capability Discovery

```csharp
// Check if driver supports specific interface
var driver = kernel.GetDriver("docker");
var caps = driver.GetCapabilities();

if (caps.Implements<IImageBuildAdvanced>())
{
    var buildDriver = driver.Images as IImageBuildAdvanced;
    // Use BuildX features
    buildDriver.BuildMultiPlatform(context, buildxParams);
}

// Check fine-grained capability
if (caps.Container.SupportsHealthChecks)
{
    var containerDriver = driver.Containers as IContainerHealth;
    var health = containerDriver.GetHealth(context, containerId);
}

// List all implemented interfaces
foreach (var iface in caps.GetImplementedInterfaces())
{
    Console.WriteLine($"Implements: {iface.Name}");
}

// Check Podman-specific features
if (caps.PodmanSpecific.SupportsPods)
{
    var podDriver = (driver as IPodDriverProvider)?.Pods;
    podDriver.Create(context, podCreateParams);
}

// Check security capabilities
Console.WriteLine($"Rootless support: {caps.Security.SupportsRootless}");
Console.WriteLine($"Default capabilities: {caps.Security.DefaultCapabilityCount}");
```

---

## Removing Fd.XXX Static Methods

### Current Fd Class Usage

The `Fd` class in `Common/Fd.cs` contains static helper methods:

```csharp
public static class Fd
{
    internal static void DisposeOnException<T>(Action<T> action, T service, string name = null)
        where T : IService
    {
        try
        {
            action.Invoke(service);
        }
        catch
        {
            Logger.Log($"Failed to run action for {name} disposing service {service.Name}");
            service.Dispose();
            throw;
        }
    }
}
```

### Migration Strategy

**Remove static methods** and provide instance-based alternatives:

1. **Move to ServiceExtensions** - Create extension methods on IService
2. **Use try-finally** - Explicit error handling
3. **Use using statements** - Automatic disposal

### Migration Examples

**v2.x.x - Using Fd.DisposeOnException:**
```csharp
Fd.DisposeOnException(svc =>
{
    var result = container.DockerHost.Execute(container.Id, command);
    if (!result.Success)
        throw new FluentDockerException($"Failed: {result.Error}");
}, service, "Execute Command");
```

**v3.0.0 - Option 1: Extension Method:**
```csharp
// ServiceExtensions.cs
public static class ServiceExtensions
{
    public static void ExecuteWithDisposal<T>(this T service, Action<T> action, string operationName = null)
        where T : IService
    {
        try
        {
            action.Invoke(service);
        }
        catch (Exception ex)
        {
            LoggerFactory.GetLogger().LogError(
                $"Failed to run '{operationName}' on service {service.Name}",
                service.Context,
                ex
            );
            service.Dispose();
            throw;
        }
    }
}

// Usage
service.ExecuteWithDisposal(svc =>
{
    var result = container.Kernel.SysCtl<IContainerDriver>(container.DriverId)
        .Execute(container.Context, container.Id, command);

    if (!result.Success)
        throw new ContainerExecutionException(container.Id, result.Error);
}, "Execute Command");
```

**v3.0.0 - Option 2: Try-Finally:**
```csharp
IContainerService container = null;
try
{
    container = new Builder(kernel)
        .UseContainer()
        .UseImage("nginx")
        .Build();

    container.Start();

    // Do work
}
catch (Exception ex)
{
    Logger.LogError($"Container operation failed: {ex.Message}");
    container?.Dispose();
    throw;
}
```

**v3.0.0 - Option 3: Using Statement (Recommended):**
```csharp
using (var container = new Builder(kernel)
    .UseContainer()
    .UseImage("nginx")
    .Build()
    .Start())
{
    // Do work - automatic disposal on exception or completion
}
```

### Migration Guide for Fd.XXX Removal

**Replace all Fd.DisposeOnException calls:**

1. **Identify usage**: Search for `Fd.DisposeOnException` in codebase
2. **Replace with using**: Prefer using statements for cleaner code
3. **Use extensions**: For complex scenarios, use ServiceExtensions
4. **Update documentation**: Remove references to Fd class

---

## Updated Error Handling for New Capabilities

### New Exceptions for Composable Interfaces

```csharp
namespace FluentDocker.Exceptions
{
    /// <summary>
    /// Exception thrown when a driver doesn't support a required sub-interface.
    /// </summary>
    public class InterfaceNotSupportedException : DriverException
    {
        public Type InterfaceType { get; set; }

        public InterfaceNotSupportedException(string driverId, Type interfaceType)
            : base($"Driver '{driverId}' does not support interface '{interfaceType.Name}'", driverId)
        {
            InterfaceType = interfaceType;
            ErrorCode = ErrorCodes.Driver.InterfaceNotSupported;
        }
    }

    /// <summary>
    /// Exception thrown when a capability is not supported.
    /// </summary>
    public class CapabilityNotSupportedException : DriverException
    {
        public string CapabilityName { get; set; }

        public CapabilityNotSupportedException(string driverId, string capabilityName)
            : base($"Driver '{driverId}' does not support capability '{capabilityName}'", driverId)
        {
            CapabilityName = capabilityName;
            ErrorCode = ErrorCodes.Driver.CapabilityNotSupported;
        }
    }
}
```

### Error Codes Update

```csharp
public static class ErrorCodes
{
    public static class Driver
    {
        // ... existing codes
        public const string InterfaceNotSupported = "DRIVER.INTERFACE_NOT_SUPPORTED";
        public const string CapabilityNotSupported = "DRIVER.CAPABILITY_NOT_SUPPORTED";
    }
}
```

### Safe Interface Access

```csharp
public static class DriverExtensions
{
    /// <summary>
    /// Safely gets a sub-interface from a driver, throwing if not supported.
    /// </summary>
    public static T GetInterface<T>(this IDriver driver, string driverId) where T : class
    {
        var caps = driver.GetCapabilities();

        if (!caps.Implements<T>())
        {
            throw new InterfaceNotSupportedException(driverId, typeof(T));
        }

        // Get the appropriate component
        if (typeof(T) == typeof(IContainerLifecycle)) return driver.Containers as T;
        if (typeof(T) == typeof(IImageBuildAdvanced)) return driver.Images as T;
        // ... other interfaces

        throw new InterfaceNotSupportedException(driverId, typeof(T));
    }

    /// <summary>
    /// Tries to get a sub-interface from a driver, returning null if not supported.
    /// </summary>
    public static T TryGetInterface<T>(this IDriver driver) where T : class
    {
        var caps = driver.GetCapabilities();

        if (!caps.Implements<T>())
            return null;

        // Get the appropriate component
        if (typeof(T) == typeof(IContainerLifecycle)) return driver.Containers as T;
        if (typeof(T) == typeof(IImageBuildAdvanced)) return driver.Images as T;
        // ... other interfaces

        return null;
    }
}

// Usage
var driver = kernel.GetDriver("docker");

// Throws if not supported
var buildAdvanced = driver.GetInterface<IImageBuildAdvanced>("docker");
buildAdvanced.BuildMultiPlatform(context, buildxParams);

// Returns null if not supported
var buildAdvanced2 = driver.TryGetInterface<IImageBuildAdvanced>();
if (buildAdvanced2 != null)
{
    buildAdvanced2.BuildMultiPlatform(context, buildxParams);
}
```

---

## Summary

### Key Enhancements

✅ **Fluent API** - Intuitive driver registration and kernel configuration
✅ **Composable Interfaces** - 30+ sub-interfaces for fine-grained implementation
✅ **Enhanced Capabilities** - Comprehensive capability system with 100+ flags
✅ **Feature Detection** - `Implements<T>()` method for interface support checking
✅ **Docker Features** - Swarm, secrets, configs, BuildX, content trust
✅ **Podman Features** - Pods, Kubernetes YAML, systemd, rootless, reduced caps
✅ **Security Capabilities** - Rootless, SELinux, AppArmor, capability counts
✅ **Performance Flags** - Streaming, bulk operations, async support
✅ **Fd.XXX Removal** - Migration to extension methods and using statements
✅ **Updated Error Handling** - New exceptions for capability/interface checks

### Benefits

**For Users:**
- Intuitive fluent configuration
- Better capability discovery
- Safer interface access
- Clearer error messages

**For Driver Implementers:**
- Implement only needed sub-interfaces
- Fine-grained capability declaration
- Clear contract definitions
- Easier testing (mock specific interfaces)

**For Maintainers:**
- Better separation of concerns
- Easier to add new features
- Clear capability matrix
- Comprehensive feature detection

The enhanced architecture provides **enterprise-grade flexibility** while maintaining **clean, intuitive APIs** for users!
