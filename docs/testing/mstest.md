---
layout: default
title: MSTest Adapter
parent: Testing
nav_order: 3
---

# MSTest Adapter

Package: `FluentDocker.Testing.MsTest`

## Step by Step

- Basics: [Helper Methods](#helper-methods), [Container Example](#container-example), [Per-Test Lifecycle](#per-test-lifecycle)
- Intermediate: [Compose Example](#compose-example), [Swarm Stack Example](#swarm-stack-example), [Podman Kubernetes Example](#podman-kubernetes-example)
- Advanced: [Generic / Custom Resource](#generic--custom-resource)

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
        var info = await _resource.InspectAsync();
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

### Swarm Stack Example

```csharp
[ClassInitialize]
public static async Task ClassInit(TestContext context)
{
    (_kernel, _resource) = await MsTestResourceHelpers.CreateSwarmStackAsync(
        new StackDeployConfig
        {
            StackName = "my-stack",
            ComposeFiles = { "docker-compose.yml" }
        });
}
```

### Podman Kubernetes Example

```csharp
[ClassInitialize]
public static async Task ClassInit(TestContext context)
{
    (_kernel, _resource) = await MsTestResourceHelpers.CreatePodmanKubernetesAsync(
        new KubePlayConfig { YamlPath = "pod.yaml" },
        kernelFactory: async () => await FluentDockerKernel.Create()
            .WithPodmanCli("podman", d => d.AsDefault())
            .BuildAsync());
}
```

### Generic / Custom Resource

Use `CreateResourceAsync<T>` for plugin or custom `ITestResource` types:

```csharp
[ClassInitialize]
public static async Task ClassInit(TestContext context)
{
    (_kernel, _resource) = await MsTestResourceHelpers.CreateResourceAsync<ContainerResource>(
        kernel => new ContainerResource(kernel,
            c => c.UseImage("redis:alpine").WaitForPort("6379/tcp")));
}
```
