# xUnit Adapter

Package: `FluentDocker.Testing.Xunit`

## Fixtures

### `XunitContainerFixture`

```csharp
public class MyFixture : XunitContainerFixture
{
    public MyFixture()
    {
        InitializeAsync(builder => builder
            .UseImage("redis:alpine")
            .WaitForPort("6379/tcp")
        ).GetAwaiter().GetResult();
    }
}

public class MyTests : IClassFixture<MyFixture>
{
    private readonly MyFixture _fixture;

    public MyTests(MyFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Redis_IsRunning()
    {
        var info = await _fixture.Container.InspectAsync();
        Assert.True(info.State.Running);
    }
}
```

### `XunitComposeFixture`

For Docker Compose-based tests:

```csharp
public class ComposeFixture : XunitComposeFixture
{
    public ComposeFixture()
    {
        InitializeAsync(builder => builder
            .WithComposeFile("docker-compose.yml")
            .WithProjectName("integration-tests")
        ).GetAwaiter().GetResult();
    }
}
```

### `XunitTopologyFixture`

For multi-container topologies. The driver is automatically bound, so there is
no need to call `WithinDriver()`:

```csharp
public class TopologyFixture : XunitTopologyFixture
{
    public TopologyFixture()
    {
        InitializeAsync(builder =>
        {
            builder.UseNetwork(n => n.WithName("test-net"));
            builder.UseContainer(c => c
                .UseImage("redis:alpine")
                .WithNetwork("test-net"));
            builder.UseContainer(c => c
                .UseImage("nginx:alpine")
                .WithNetwork("test-net"));
        }).GetAwaiter().GetResult();
    }
}
```

### `XunitSwarmStackFixture`

For Docker Swarm stack deployments:

```csharp
public class SwarmFixture : XunitSwarmStackFixture
{
    public SwarmFixture()
    {
        InitializeAsync(new StackDeployConfig
        {
            StackName = "my-stack",
            ComposeFiles = { "docker-compose.yml" }
        }).GetAwaiter().GetResult();
    }
}
```

### `XunitPodmanKubernetesFixture`

For Podman Kubernetes YAML deployments:

```csharp
public class KubeFixture : XunitPodmanKubernetesFixture
{
    public KubeFixture()
    {
        InitializeAsync(new KubePlayConfig
        {
            YamlPath = "pod.yaml"
        }).GetAwaiter().GetResult();
    }
}
```

### `XunitResourceFixture<TResource>`

Generic fixture for any `ITestResource`, including plugin resources:

```csharp
public class MyPluginFixture : XunitResourceFixture<ContainerResource>
{
    public MyPluginFixture()
    {
        InitializeAsync(kernel =>
            new ContainerResource(kernel,
                c => c.UseImage("redis:alpine").WaitForPort("6379/tcp"))
        ).GetAwaiter().GetResult();
    }
}
```

## Custom Kernel

All fixtures accept an optional kernel factory:

```csharp
await InitializeAsync(
    configure: builder => builder.UseImage("alpine:latest"),
    kernelFactory: async () => await FluentDockerKernel.Create()
        .WithPodmanCli("podman", d => d.AsDefault())
        .BuildAsync()
);
```

## Fixture Lifecycle

Fixtures implement `IAsyncDisposable`. After `DisposeAsync()`, the `Resource`
and `Kernel` properties are cleared to `null`, allowing re-initialization if
needed. The `GetAwaiter().GetResult()` pattern in constructors is required
because xUnit does not support async fixture constructors, and the
fixture's `InitializeAsync` takes configuration parameters that cannot be
passed through the parameterless `IAsyncLifetime.InitializeAsync()`.
