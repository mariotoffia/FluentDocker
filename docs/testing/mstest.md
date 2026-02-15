# MSTest Adapter

Package: `FluentDocker.Testing.MsTest`

## Helper Methods

`MsTestResourceHelpers` provides static async methods for creating and disposing resources.

### Container Example

```csharp
[TestClass]
public class RedisTests
{
    private static FluentDockerKernel _kernel;
    private static ContainerResource _resource;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        (_kernel, _resource) = await MsTestResourceHelpers.CreateContainerAsync(
            builder => builder
                .UseImage("redis:alpine")
                .WaitForPort("6379/tcp"));
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
    }

    [TestMethod]
    public async Task Redis_IsRunning()
    {
        var info = await _resource.Container.InspectAsync();
        Assert.IsTrue(info.State.Running);
    }
}
```

### Per-Test Lifecycle

```csharp
[TestClass]
public class PerTestRedisTests
{
    private FluentDockerKernel _kernel;
    private ContainerResource _resource;

    [TestInitialize]
    public async Task Setup()
    {
        (_kernel, _resource) = await MsTestResourceHelpers.CreateContainerAsync(
            builder => builder.UseImage("redis:alpine"));
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);
    }
}
```

### Compose Example

```csharp
[ClassInitialize]
public static async Task ClassInit(TestContext context)
{
    (_kernel, _resource) = await MsTestResourceHelpers.CreateComposeAsync(
        builder => builder
            .WithComposeFile("docker-compose.yml")
            .WithProjectName("integration-tests"));
}
```
