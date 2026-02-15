# NUnit Adapter

Package: `FluentDocker.Testing.NUnit`

## Helper Methods

`NUnitResourceHelpers` provides static async methods for use with NUnit's `[OneTimeSetUp]`
and `[OneTimeTearDown]`, or per-test `[SetUp]`/`[TearDown]`.

### OneTimeSetUp Example

```csharp
[TestFixture]
public class RedisTests
{
    private FluentDockerKernel _kernel;
    private ContainerResource _resource;

    [OneTimeSetUp]
    public async Task Setup()
    {
        (_kernel, _resource) = await NUnitResourceHelpers.CreateContainerAsync(
            builder => builder
                .UseImage("redis:alpine")
                .WaitForPort("6379/tcp"));
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await NUnitResourceHelpers.DisposeAsync(_resource, _kernel);
    }

    [Test]
    public async Task Redis_IsRunning()
    {
        var info = await _resource.Container.InspectAsync();
        Assert.That(info.State.Running, Is.True);
    }
}
```

### Assembly-Level SetUpFixture

```csharp
[SetUpFixture]
public class GlobalDockerSetup
{
    private FluentDockerKernel _kernel;
    private ContainerResource _resource;

    [OneTimeSetUp]
    public async Task Setup()
    {
        (_kernel, _resource) = await NUnitResourceHelpers.CreateContainerAsync(
            builder => builder.UseImage("redis:alpine"));
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await NUnitResourceHelpers.DisposeAsync(_resource, _kernel);
    }
}
```

### Swarm Stack Example

```csharp
[OneTimeSetUp]
public async Task Setup()
{
    (_kernel, _resource) = await NUnitResourceHelpers.CreateSwarmStackAsync(
        new StackDeployConfig
        {
            StackName = "my-stack",
            ComposeFiles = { "docker-compose.yml" }
        });
}
```

### Podman Kubernetes Example

```csharp
[OneTimeSetUp]
public async Task Setup()
{
    (_kernel, _resource) = await NUnitResourceHelpers.CreatePodmanKubernetesAsync(
        new KubePlayConfig { YamlPath = "pod.yaml" },
        kernelFactory: async () => await FluentDockerKernel.Create()
            .WithPodmanCli("podman", d => d.AsDefault())
            .BuildAsync());
}
```

### Generic / Custom Resource

Use `CreateResourceAsync<T>` for plugin or custom `ITestResource` types:

```csharp
[OneTimeSetUp]
public async Task Setup()
{
    (_kernel, _resource) = await NUnitResourceHelpers.CreateResourceAsync<ContainerResource>(
        kernel => new ContainerResource(kernel,
            c => c.UseImage("redis:alpine").WaitForPort("6379/tcp")));
}
```
