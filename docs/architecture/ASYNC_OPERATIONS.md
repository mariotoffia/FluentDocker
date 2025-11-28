# Async Operations - FluentDocker v3.0.0

## Overview

**All operations in FluentDocker v3.0.0 are asynchronous**. This provides:
- Non-blocking execution
- Better resource utilization
- Parallel execution where possible
- Modern .NET async/await pattern

## Core Principles

1. **BuildAsync() is terminal** - Returns `Task<TResult>`
2. **All driver operations are async** - Return `Task<CommandResponse<T>>`
3. **All service operations are async** - `StartAsync()`, `StopAsync()`, etc.
4. **Cancellation support** - All async methods accept `CancellationToken`

## Async Builder Pattern

### Builder Interface

```csharp
public interface IFluentBuilder
{
    Builder WithinDriver(string driverId, FluentDockerKernel kernel = null);
    Builder UseContainer(Action<IContainerBuilder> configure);
    Builder UseCompose(Action<IComposeBuilder> configure);
    Builder UseNetwork(Action<INetworkBuilder> configure);
    Builder UseVolume(Action<IVolumeBuilder> configure);
    
    // TERMINAL - async
    Task<BuildResults> BuildAsync(CancellationToken cancellationToken = default);
}
```

### Builder Implementation

```csharp
public class Builder : IFluentBuilder
{
    private FluentDockerKernel _currentKernel;
    private string _currentDriverId;
    private readonly List<BuildOperation> _operations = new();

    public Builder WithinDriver(string driverId, FluentDockerKernel kernel = null)
    {
        _currentKernel = kernel ?? _currentKernel;
        if (_currentKernel == null)
            throw new InvalidOperationException("Kernel required in first WithinDriver() call");
            
        _currentDriverId = driverId;
        return this;
    }

    public Builder UseContainer(Action<IContainerBuilder> configure)
    {
        ValidateScope();
        var builder = new ContainerBuilder(_currentKernel, _currentDriverId);
        configure(builder);
        
        _operations.Add(new BuildOperation
        {
            Kernel = _currentKernel,
            DriverId = _currentDriverId,
            ExecuteAsync = ct => builder.ExecuteAsync(ct)
        });
        
        return this;
    }

    // TERMINAL - executes all operations asynchronously
    public async Task<BuildResults> BuildAsync(CancellationToken cancellationToken = default)
    {
        var scopes = new Dictionary<(FluentDockerKernel, string), BuildScope>();

        // Group by scope
        var groupedOps = _operations.GroupBy(op => (op.Kernel, op.DriverId));

        foreach (var group in groupedOps)
        {
            var key = group.Key;
            scopes[key] = new BuildScope(key.Kernel, key.DriverId);

            // Execute operations (can be parallelized if needed)
            foreach (var operation in group)
            {
                var service = await operation.ExecuteAsync(cancellationToken);
                scopes[key].AddResult(service);
            }
        }

        return new BuildResults(scopes.Values.ToList());
    }

    private void ValidateScope()
    {
        if (_currentKernel == null || _currentDriverId == null)
            throw new InvalidOperationException("Must call WithinDriver() first");
    }
}

internal class BuildOperation
{
    public FluentDockerKernel Kernel { get; set; }
    public string DriverId { get; set; }
    public Func<CancellationToken, Task<IService>> ExecuteAsync { get; set; }
}
```

## Async Driver Interfaces

### Container Driver

```csharp
public interface IContainerDriver
{
    Task<CommandResponse<ContainerCreateResult>> CreateAsync(
        DriverContext context,
        ContainerCreateConfig config,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<Unit>> StartAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<Unit>> StopAsync(
        DriverContext context,
        string containerId,
        int? timeout = null,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string containerId,
        bool force = false,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<Container>> InspectAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<IList<Container>>> ListAsync(
        DriverContext context,
        ContainerListFilter filter = null,
        CancellationToken cancellationToken = default);
}
```

### Image Driver

```csharp
public interface IImageDriver
{
    Task<CommandResponse<Unit>> PullAsync(
        DriverContext context,
        string image,
        string tag = "latest",
        IProgress<ImagePullProgress> progress = null,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string imageId,
        bool force = false,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<ImageBuildResult>> BuildAsync(
        DriverContext context,
        ImageBuildConfig config,
        IProgress<ImageBuildProgress> progress = null,
        CancellationToken cancellationToken = default);

    Task<CommandResponse<IList<Image>>> ListAsync(
        DriverContext context,
        ImageListFilter filter = null,
        CancellationToken cancellationToken = default);
}
```

## Async Service Interfaces

### Container Service

```csharp
public interface IContainerService : IService
{
    FluentDockerKernel Kernel { get; }
    string DriverId { get; }
    string Id { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(int? timeout = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default);
    Task<Container> InspectAsync(bool refresh = false, CancellationToken cancellationToken = default);
    Task<string> GetLogsAsync(CancellationToken cancellationToken = default);
    
    // Async disposal
    ValueTask DisposeAsync();
}
```

### Implementation

```csharp
public class DockerContainerService : IContainerService, IAsyncDisposable
{
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;
    private readonly string _id;

    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;
    public string Id => _id;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
        var context = new DriverContext { DriverId = _driverId };
        
        var response = await driver.StartAsync(context, _id, cancellationToken);
        
        if (!response.Success)
        {
            throw new ContainerStartException(
                $"Failed to start container: {response.Error}",
                response.ErrorCode,
                response.Context);
        }
    }

    public async Task StopAsync(int? timeout = null, CancellationToken cancellationToken = default)
    {
        var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
        var context = new DriverContext { DriverId = _driverId };
        
        var response = await driver.StopAsync(context, _id, timeout, cancellationToken);
        
        if (!response.Success)
        {
            throw new ContainerStopException(
                $"Failed to stop container: {response.Error}",
                response.ErrorCode,
                response.Context);
        }
    }

    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
        var context = new DriverContext { DriverId = _driverId };
        
        var response = await driver.RemoveAsync(context, _id, force, cancellationToken);
        
        if (!response.Success)
        {
            throw new ContainerRemoveException(
                $"Failed to remove container: {response.Error}",
                response.ErrorCode,
                response.Context);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
            await RemoveAsync(force: true);
        }
        catch
        {
            // Log but don't throw in disposal
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
```

## Async Kernel Operations

### Kernel Builder

```csharp
public interface IKernelBuilder
{
    IKernelBuilder WithDriver(string driverId, Action<IDriverBuilder> configure);
    IKernelBuilder WithRetryPolicy(Action<IRetryPolicyBuilder> configure);
    IKernelBuilder WithLogging(Action<ILoggingBuilder> configure);
    IKernelBuilder WithMetrics<T>() where T : IFluentDockerMetrics, new();
    
    // TERMINAL - async
    Task<FluentDockerKernel> BuildAsync(CancellationToken cancellationToken = default);
}
```

### Implementation

```csharp
public class KernelBuilder : IKernelBuilder
{
    private readonly List<Func<CancellationToken, Task>> _configurations = new();
    private readonly FluentDockerKernelOptions _options = new();

    public static IKernelBuilder Create() => new KernelBuilder();

    public IKernelBuilder WithDriver(string driverId, Action<IDriverBuilder> configure)
    {
        _configurations.Add(async ct =>
        {
            var builder = new DriverBuilder();
            configure(builder);
            await builder.RegisterAsync(driverId, _options, ct);
        });
        return this;
    }

    public async Task<FluentDockerKernel> BuildAsync(CancellationToken cancellationToken = default)
    {
        var kernel = new FluentDockerKernel(_options);
        
        // Execute all configurations
        foreach (var config in _configurations)
        {
            await config(cancellationToken);
        }
        
        return kernel;
    }
}
```

## Usage Examples

### Basic Container Creation

```csharp
public async Task CreateContainerAsync()
{
    var kernel = await FluentDockerKernel.Create()
        .WithDriver("docker", d => d.UseDockerCli())
        .BuildAsync();

    var results = await new Builder()
        .WithinDriver("docker", kernel)
            .UseContainer(c => c
                .UseImage("nginx")
                .WithName("web"))
        .BuildAsync();

    var container = results.All[0] as IContainerService;
    await container.StartAsync();

    // Do work...

    await container.StopAsync();
    await container.DisposeAsync();
}
```

### Multi-Environment Deployment

```csharp
public async Task DeployAsync(CancellationToken cancellationToken)
{
    var kernel = await FluentDockerKernel.Create()
        .WithDriver("dev", d => d.UseDockerCli())
        .WithDriver("prod", d => d
            .UseDockerApi()
            .AtHost("tcp://prod:2376")
            .WithCertificates("/certs"))
        .BuildAsync(cancellationToken);

    var deployment = await new Builder()
        .WithinDriver("dev", kernel)
            .UseContainer(c => c.UseImage("myapp:dev"))
            .UseContainer(c => c.UseImage("postgres:14"))
        .WithinDriver("prod")  // Reuses kernel
            .UseContainer(c => c.UseImage("myapp:v1.0"))
        .BuildAsync(cancellationToken);

    // Start all services in parallel
    var startTasks = deployment.All
        .OfType<IContainerService>()
        .Select(c => c.StartAsync(cancellationToken));
    
    await Task.WhenAll(startTasks);

    // Cleanup
    await deployment.DisposeAllAsync();
}
```

### Progress Reporting

```csharp
public async Task PullImageWithProgressAsync()
{
    var kernel = await FluentDockerKernel.Create()
        .WithDriver("docker", d => d.UseDockerCli())
        .BuildAsync();

    var driver = kernel.SysCtl<IImageDriver>("docker");
    var context = new DriverContext { DriverId = "docker" };

    var progress = new Progress<ImagePullProgress>(p =>
    {
        Console.WriteLine($"Pulling: {p.Status} - {p.Progress}");
    });

    var response = await driver.PullAsync(
        context,
        "nginx",
        "latest",
        progress,
        CancellationToken.None);

    if (response.Success)
    {
        Console.WriteLine("Image pulled successfully!");
    }
}
```

### Cancellation Support

```csharp
public async Task DeployWithCancellationAsync()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

    try
    {
        var kernel = await FluentDockerKernel.Create()
            .WithDriver("docker", d => d.UseDockerCli())
            .BuildAsync(cts.Token);

        var deployment = await new Builder()
            .WithinDriver("docker", kernel)
                .UseContainer(c => c.UseImage("nginx"))
            .BuildAsync(cts.Token);

        await deployment.All[0].StartAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Deployment cancelled");
    }
}
```

## BuildResults with Async Disposal

```csharp
public class BuildResults : IAsyncDisposable
{
    private readonly List<BuildScope> _scopes;

    public BuildResults(List<BuildScope> scopes) => _scopes = scopes;

    public IReadOnlyList<IService> All =>
        _scopes.SelectMany(s => s.Results).ToList();

    public IReadOnlyList<IService> ForDriver(string driverId) =>
        _scopes.Where(s => s.DriverId == driverId)
               .SelectMany(s => s.Results)
               .ToList();

    // Async disposal
    public async ValueTask DisposeAsync()
    {
        foreach (var service in All)
        {
            if (service is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                service?.Dispose();
            }
        }
    }

    // Explicit async disposal
    public async Task DisposeAllAsync()
    {
        await DisposeAsync();
    }

    // Sync disposal (calls async version)
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public void DisposeAll() => Dispose();
}
```

## Benefits of Async Pattern

1. **Non-blocking** - UI/server threads not blocked during Docker operations
2. **Scalability** - Better resource utilization
3. **Cancellation** - Operations can be cancelled
4. **Progress reporting** - Long-running operations can report progress
5. **Parallel execution** - Multiple operations can run concurrently
6. **Modern .NET** - Follows current best practices
7. **Better error handling** - Async exceptions are easier to handle

## Migration from Sync to Async

**Sync (OLD):**
```csharp
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker", d => d.UseDockerCli())
    .Build();

var results = new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer(c => c.UseImage("nginx"))
    .Build();

results.All[0].Start();
```

**Async (NEW):**
```csharp
var kernel = await FluentDockerKernel.Create()
    .WithDriver("docker", d => d.UseDockerCli())
    .BuildAsync();

var results = await new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer(c => c.UseImage("nginx"))
    .BuildAsync();

await results.All[0].StartAsync();
```

**Key Changes:**
- `Build()` → `BuildAsync()` with `await`
- `.Start()` → `await .StartAsync()`
- `.Stop()` → `await .StopAsync()`
- All driver calls are `async`
- Add `CancellationToken` support
- Implement `IAsyncDisposable`
