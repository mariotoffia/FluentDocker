# FluentDocker

[![CI](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml/badge.svg)](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/mariotoffia/FluentDocker/branch/master/graph/badge.svg)](https://codecov.io/gh/mariotoffia/FluentDocker)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-net10.0-blueviolet)](https://dotnet.microsoft.com/)

| Package | NuGet | Downloads |
|---------|:-----:|:---------:|
| FluentDocker | [![NuGet](https://img.shields.io/nuget/v/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker) | [![Downloads](https://img.shields.io/nuget/dt/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker) |
| Testing.Xunit | [![NuGet](https://img.shields.io/nuget/v/FluentDocker.Testing.Xunit.svg)](https://www.nuget.org/packages/FluentDocker.Testing.Xunit) | [![Downloads](https://img.shields.io/nuget/dt/FluentDocker.Testing.Xunit.svg)](https://www.nuget.org/packages/FluentDocker.Testing.Xunit) |
| Testing.MsTest | [![NuGet](https://img.shields.io/nuget/v/FluentDocker.Testing.MsTest.svg)](https://www.nuget.org/packages/FluentDocker.Testing.MsTest) | [![Downloads](https://img.shields.io/nuget/dt/FluentDocker.Testing.MsTest.svg)](https://www.nuget.org/packages/FluentDocker.Testing.MsTest) |
| Testing.NUnit | [![NuGet](https://img.shields.io/nuget/v/FluentDocker.Testing.NUnit.svg)](https://www.nuget.org/packages/FluentDocker.Testing.NUnit) | [![Downloads](https://img.shields.io/nuget/dt/FluentDocker.Testing.NUnit.svg)](https://www.nuget.org/packages/FluentDocker.Testing.NUnit) |

---

## What's New in v3.0.0

FluentDocker v3.0.0 is a major release with significant improvements:

- **Namespace renamed**: `Ductus.FluentDocker` → `FluentDocker`
- **Full async/await support** with CancellationToken throughout
- **Driver Layer architecture** replacing Commands namespace
- **Kernel + typed driver scoping** — `WithinDockerCli()`, `WithinDockerApi()`, `WithinPodmanCli()`
- **Type-safe builder API** — compile-time safety for driver-specific features
- **Lambda-based builder API** — `UseContainer(Action<IContainerBuilder>)`
- **Container Stats** — CPU, memory, network, and block I/O monitoring
- **Label-based filtering** — 5.5x faster container cleanup
- **Static IPv4/IPv6** assignment for containers
- **Directory copy** support (recursive copy to/from containers)
- **Docker Compose V2** — uses `docker compose` subcommand

### Breaking Changes

- `Ductus.FluentDocker` namespace → `FluentDocker`
- Builder API now requires `WithinDriver()` scoping and lambda-based configuration
- `Build()` returns `BuildResults` (containers auto-start; no separate `.Start()`)
- Docker Machine support **removed** (deprecated by Docker)
- Docker Toolbox support **removed** (deprecated by Docker)
- Commands namespace **removed** (use Driver Layer)
- Compose commands use struct-based arguments

💡 **LLM Skill to semi-automate the upgrade from V2 -> V3**

📖 **[Migration Guide →](docs/migration.md)**

---

## Quick Start

```csharp
using System.Linq;
using FluentDocker.Builders;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

// Multiple kernels per app are supported.
// This kernel registers both Docker CLI and Podman CLI.
using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .WithPodmanCli("podman", d => d.WithAutoStartMachine())
    .BuildAsync();
```

### 1) Standard container (Docker CLI)

```csharp
await using var results = await new Builder()
    .WithinDockerCli("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();

var endpoint = results.Containers.First()
    .ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"Docker endpoint: {endpoint.Address}:{endpoint.Port}");
```

### 2) Standard container (Podman CLI)

```csharp
await using var results = await new Builder()
    .WithinPodmanCli("podman", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();

var endpoint = results.Containers.First()
    .ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"Podman endpoint: {endpoint.Address}:{endpoint.Port}");
```

### 3) Docker Compose (Docker CLI)

```csharp
await using var results = await new Builder()
    .WithinDockerCli("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans()
        .WithWait()
        .WithWaitTimeout(30))
    .BuildAsync();

var compose = results.ComposeServices.First();
```

### 4) Podman Kubernetes (kube play / kube down)

```csharp
var context = new DriverContext("podman");
var kube = kernel.SysCtl<IPodmanKubernetesDriver>("podman");

await kube.PlayAsync(context, new KubePlayConfig
{
    YamlPath = "pod.yaml",
    Replace = true
});

// Teardown
await kube.DownAsync(context, "pod.yaml");
```

---

## Installation

```bash
dotnet add package FluentDocker
dotnet add package FluentDocker.Testing.Xunit   # xUnit adapter
dotnet add package FluentDocker.Testing.MsTest  # MSTest adapter
dotnet add package FluentDocker.Testing.NUnit   # NUnit adapter
```

---

## Features

### Container Management

```csharp
// Create and start
using var results = new Builder()
    .WithinDockerCli("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:latest")
        .ExposePort("80"))
    .Build();

var container = results.Containers.First();

// Get configuration
var config = container.GetConfiguration(true);

// Container stats (v3)
var stats = await container.GetStatsAsync();
Console.WriteLine($"CPU: {stats.CpuPercent:F2}%");
```

### Port Mapping

```csharp
// Explicit: host port 8080 → container port 80
.ExposePort(8080, 80)

// Random: let Docker choose host port
.ExposePort("80")

// Resolve actual endpoint
var endpoint = container.ToHostExposedEndpoint("80/tcp");
```

### Wait Strategies

```csharp
.WaitForPort("5432/tcp", 30000)           // Wait for port
.WaitForProcess("postgres", 30000)         // Wait for process
.WaitForLogMessage("ready", 30000)         // Wait for log message
```

### Networks with Static IP

```csharp
using var nwResults = new Builder()
    .WithinDockerCli("docker", kernel)
    .UseNetwork(n => n
        .WithName("my-network")
        .WithSubnet("10.18.0.0/16"))
    .Build();

var network = nwResults.Networks.First();

using var cResults = new Builder()
    .WithinDockerCli("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx")
        .WithNetwork("my-network")
        .UseIpV4("10.18.0.100"))
    .Build();
```

### Volume Mounts

```csharp
// Host path mount
.WithVolume("/host/path", "/container/path")

// Named volume
.WithVolume("my-vol", "/data")
```

### File Operations

```csharp
// Copy to container
await container.CopyToAsync("/local/file", "/container/file");
await container.CopyToAsync("/local/dir", "/container/dir");  // v3: directories

// Copy from container
await container.CopyFromAsync("/container/file", "/local/file");
```

### Image Building

```csharp
// Inline Dockerfile
using var imgResults = new Builder()
    .WithinDockerCli("docker", kernel)
    .UseImage("mynode:latest", img => img
        .From("node:18-alpine")
        .Run("npm install -g nodemon")
        .ExposePorts(8080)
        .Command("node", "app.js"))
    .Build();
```

---

## Drivers

FluentDocker ships with three drivers:

- Docker CLI
- Docker API
- Podman CLI

All drivers share a common core (`IContainerDriver`, `IImageDriver`,
`INetworkDriver`, `IVolumeDriver`, `ISystemDriver`, `IAuthDriver`,
`IStreamDriver`) and add driver-specific capabilities on top.

### Quick Driver Examples (Start Here)

Use these imports in the snippets below:

```csharp
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
```

Example: create a kernel with both Docker CLI and Podman CLI
(multiple kernels per app are also supported):

```csharp
using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .WithPodmanCli("podman", d => d
        .WithAutoStartMachine() // macOS/Windows: ensure Podman VM is running
        .AsDefault())
    .BuildAsync();
```

#### 1) Standard container (Docker CLI)

```csharp
await using var dockerResults = await new Builder()
    .WithinDockerCli("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();

var dockerEndpoint = dockerResults.Containers.First()
    .ToHostExposedEndpoint("80/tcp");
```

#### 2) Standard container (Podman CLI)

```csharp
await using var podmanResults = await new Builder()
    .WithinPodmanCli("podman", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();

var podmanEndpoint = podmanResults.Containers.First()
    .ToHostExposedEndpoint("80/tcp");
```

#### 3) Docker Compose (Docker CLI)

```csharp
await using var composeResults = await new Builder()
    .WithinDockerCli("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans()
        .WithWait())
    .BuildAsync();

var compose = composeResults.ComposeServices.First();
```

#### 4) Kubernetes play/down (Podman CLI)

```csharp
var podmanContext = new DriverContext("podman");
var kube = kernel.SysCtl<IPodmanKubernetesDriver>("podman");

var play = await kube.PlayAsync(podmanContext,
    new KubePlayConfig
    {
        YamlPath = "pod.yaml",
        Replace = true
    });

// Teardown when done
await kube.DownAsync(podmanContext, "pod.yaml");
```

#### 5) Swarm stack deploy/remove (Docker CLI)

```csharp
// Requires Docker Swarm mode: docker swarm init
var dockerContext = new DriverContext("docker");
var stacks = kernel.SysCtl<IStackDriver>("docker");

var deploy = await stacks.DeployAsync(dockerContext,
    new StackDeployConfig
    {
        StackName = "web",
        ComposeFiles = new List<string> { "docker-stack.yml" }
    });

var services = await stacks.GetServicesAsync(dockerContext, "web");

// Teardown when done
await stacks.RemoveAsync(dockerContext, new[] { "web" });
```

### Capability per Driver

| Capability | Docker CLI | Docker API | Podman CLI |
|---|:---:|:---:|:---:|
| Container / Image / Network / Volume | yes | yes | yes |
| System / Auth / Streaming | yes | yes | yes |
| Compose | yes | - | - |
| Stack (Swarm) | yes | - | - |
| Service (Swarm) | yes | yes | - |
| Pods | - | - | yes |
| Kubernetes play/generate | - | - | yes |
| Machine management | - | - | yes |
| Multi-arch manifests | - | - | yes |

### Kernel Setup

Register one or more drivers in the kernel builder:

```csharp
using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .WithDockerApi("docker-api", d => d
        .WithConnectionTimeout(TimeSpan.FromSeconds(30)))
    .WithPodmanCli("podman", d => d
        .WithAutoStartMachine()
        .AsDefault())
    .BuildAsync();
```

### Common API - Works with Any Driver

The fluent builder API is shared across drivers. Switch driver scope and keep
the same container definition style.

```csharp
await using var results = await new Builder()
    .WithinDriver(driverId, kernel) // driverId: "docker", "docker-api", "podman"
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .ExposePort("5432")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WaitForPort("5432/tcp", 30000))
    .BuildAsync();
```

### Docker API — Direct Engine Communication

The Docker API driver talks directly to the Docker Engine REST API over Unix
socket, named pipe, or TCP+TLS. No Docker CLI binary is required.

```csharp
using var kernel = await FluentDockerKernel.Create()
    .WithDockerApi("api", d => d
        .AtHost("unix:///var/run/docker.sock")    // optional, auto-detected
        .WithCertificates("/path/to/certs")       // optional, for TLS
        .WithConnectionTimeout(TimeSpan.FromSeconds(15))
        .WithRequestTimeout(TimeSpan.FromMinutes(10))
        .AsDefault())
    .BuildAsync();

await using var results = await new Builder()
    .WithinDockerApi("api", kernel)
    .UseContainer(c => c
        .UseImage("redis:7-alpine")
        .ExposePort("6379"))
    .BuildAsync();

var stream = kernel.SysCtl<IStreamDriver>("api");
var context = new DriverContext("api");
await foreach (var ev in stream.StreamEventsAsync(context))
    Console.WriteLine($"Event: {ev.Action} on {ev.Type}");
```

### Podman-Specific Features via Driver Layer

Access Podman-only capabilities through `SysCtl<T>` or `TrySysCtl<T>`:

```csharp
var context = new DriverContext("podman");

// Pods
var pods = kernel.SysCtl<IPodmanPodDriver>("podman");
await pods.CreatePodAsync(context, new PodCreateConfig { Name = "my-pod" });

// Machine management
var machines = kernel.SysCtl<IPodmanMachineDriver>("podman");
var list = await machines.ListAsync(context);

// Multi-arch manifests
var manifest = kernel.SysCtl<IPodmanManifestDriver>("podman");
await manifest.CreateAsync(context,
    new ManifestCreateConfig
    {
        Name = "myapp:latest",
        Images = new List<string> { "myapp:amd64", "myapp:arm64" }
    });
```

### Writing Driver-Portable Code

Use `TrySysCtl<T>` when you want optional driver-specific behavior:

```csharp
if (kernel.TrySysCtl<IPodmanPodDriver>(driverId, out var podDriver))
{
    await podDriver.CreatePodAsync(context,
        new PodCreateConfig { Name = "my-pod" });
}
```

---

## Test Support

FluentDocker v3 includes `FluentDocker.Testing.Core` in the main assembly.
Framework-specific adapters are available as separate packages.

### xUnit (Testing.Core)

```csharp
using FluentDocker.Testing.Xunit;

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

### MSTest (Testing.Core)

```csharp
using FluentDocker.Testing.MsTest;

[TestClass]
public class MyTests
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
}
```

See the [full testing docs](docs/testing.md) for NUnit, Compose, Topology,
Swarm Stack, and Podman Kubernetes resource types.

---

## Linux Users

Docker requires sudo by default. Configure per-driver:
`WithSudo(SudoMechanism.NoPassword)` or add user to docker group:
`sudo usermod -aG docker $USER`

---

## Contributing

Contributions welcome! Please adhere to `.editorconfig` for code style.

---

## Resources

- [Documentation Site](https://mariotoffia.github.io/FluentDocker/) - Full documentation on GitHub Pages
- [Migration Guide](docs/migration.md) - Upgrading from v2.x.x
- [Architecture Docs](docs/architecture.md) - v3 architecture details
- [NuGet Package](https://www.nuget.org/packages/FluentDocker)

---

## License

Apache 2.0 - See [LICENSE](LICENSE) for details.
