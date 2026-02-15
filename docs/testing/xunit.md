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

For multi-container topologies:

```csharp
public class TopologyFixture : XunitTopologyFixture
{
    public TopologyFixture()
    {
        InitializeAsync(builder =>
        {
            builder.WithinDockerCli("docker-cli", kernel);
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
