# FluentDocker

[![CI](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml/badge.svg)](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/mariotoffia/FluentDocker/branch/master/graph/badge.svg)](https://codecov.io/gh/mariotoffia/FluentDocker)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-net10.0-blueviolet)](https://dotnet.microsoft.com/)

| Package | NuGet | Downloads |
|---------|:-----:|:---------:|
| FluentDocker | [![NuGet](https://img.shields.io/nuget/v/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker) | [![Downloads](https://img.shields.io/nuget/dt/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker) |
| MsTest | [![NuGet](https://img.shields.io/nuget/v/FluentDocker.MsTest.svg)](https://www.nuget.org/packages/FluentDocker.MsTest) | [![Downloads](https://img.shields.io/nuget/dt/FluentDocker.MsTest.svg)](https://www.nuget.org/packages/FluentDocker.MsTest) |
| XUnit | [![NuGet](https://img.shields.io/nuget/v/FluentDocker.XUnit.svg)](https://www.nuget.org/packages/FluentDocker.XUnit) | [![Downloads](https://img.shields.io/nuget/dt/FluentDocker.XUnit.svg)](https://www.nuget.org/packages/FluentDocker.XUnit) |

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

📖 **[Migration Guide →](docs/migration.md)**

---

## Quick Start

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;

// 1. Create a kernel (once per application)
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();

// 2. Start a container and wait for it to be ready
using var results = new Builder()
    .WithinDockerCli("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .ExposePort("5432")
        .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
        .WaitForPort("5432/tcp", 30000))
    .Build();

var container = results.Containers.First();
var endpoint = container.ToHostExposedEndpoint("5432/tcp");
Console.WriteLine($"Connect to: {endpoint.Address}:{endpoint.Port}");
```

### Docker Compose

```csharp
using var results = new Builder()
    .WithinDockerCli("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans()
        .WithWait()
        .WithWaitTimeout(30))
    .Build();

// Access individual containers
var compose = results.ComposeServices.First();
```

---

## Installation

```bash
dotnet add package FluentDocker
dotnet add package FluentDocker.MsTest  # Optional
dotnet add package FluentDocker.XUnit   # Optional
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

FluentDocker ships with three drivers. All share a common set of interfaces
(`IContainerDriver`, `IImageDriver`, `INetworkDriver`, `IVolumeDriver`,
`ISystemDriver`, `IAuthDriver`, `IStreamDriver`) and each adds
driver-specific capabilities on top.

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

Each driver is registered with a kernel through the builder API.
You can register multiple drivers in the same kernel.

```csharp
using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .WithDockerApi("docker-api", d => d
        .WithConnectionTimeout(TimeSpan.FromSeconds(30)))
    .WithPodmanCli("podman", d => d
        .WithAutoStartMachine()   // auto-start Podman VM on macOS/Windows
        .AsDefault())
    .BuildAsync();
```

### Common API — Works with Any Driver

The builder API and shared interfaces work identically regardless of which
driver is active. Swap the driver ID and everything else stays the same.

```csharp
// Type-safe: WithinPodmanCli exposes only Podman-valid operations
await using var results = await new Builder()
    .WithinPodmanCli("podman", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .ExposePort("5432")
        .WithEnvironment("POSTGRES_PASSWORD=secret")
        .WaitForPort("5432/tcp", 30000))
    .BuildAsync();

var endpoint = results.Containers.First()
    .ToHostExposedEndpoint("5432/tcp");
```

The generic `WithinDriver()` still works for driver-portable code:

```csharp
// Generic: works with any driver ID
await using var results = await new Builder()
    .WithinDriver(driverId, kernel)
    .UseContainer(c => c.UseImage("alpine:latest"))
    .BuildAsync();
```

### Docker CLI — Compose and Swarm

The Docker CLI driver adds Compose and Swarm support.

```csharp
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();

// Docker Compose (V2 — "docker compose" subcommand)
// UseCompose() is only visible on DockerCliFluentBuilder
await using var results = await new Builder()
    .WithinDockerCli("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans()
        .WithWait())
    .BuildAsync();
```

### Docker API — Direct Engine Communication

The Docker API driver talks directly to the Docker Engine REST API over
Unix socket, named pipe, or TCP+TLS. No CLI binary needed. It auto-detects
the transport and negotiates the API version.

```csharp
using var kernel = await FluentDockerKernel.Create()
    .WithDockerApi("api", d => d
        .AtHost("unix:///var/run/docker.sock")    // optional, auto-detected
        .WithCertificates("/path/to/certs")       // optional, for TLS
        .WithConnectionTimeout(TimeSpan.FromSeconds(15))
        .WithRequestTimeout(TimeSpan.FromMinutes(10))
        .AsDefault())
    .BuildAsync();

// Use the common builder API
await using var results = await new Builder()
    .WithinDockerApi("api", kernel)
    .UseContainer(c => c
        .UseImage("redis:7-alpine")
        .ExposePort("6379"))
    .BuildAsync();

// Or access the driver layer directly for streaming
var stream = kernel.SysCtl<IStreamDriver>("api");
var context = new DriverContext("api");
await foreach (var ev in stream.StreamEventsAsync(context))
    Console.WriteLine($"Event: {ev.Action} on {ev.Type}");
```

### Podman CLI — Pods, Kubernetes, Machines

The Podman driver adds pod management, Kubernetes integration,
machine lifecycle, and multi-arch manifest support.

```csharp
using var kernel = await FluentDockerKernel.Create()
    .WithPodmanCli("podman", d => d
        .WithAutoStartMachine()   // ensures VM is running on macOS/Windows
        .AsDefault())
    .BuildAsync();

// Common builder API works the same; UsePod() only visible here
await using var results = await new Builder()
    .WithinPodmanCli("podman", kernel)
    .UsePod(p => p.WithName("my-pod").WithPort("8080", "80"))
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80"))
    .BuildAsync();
```

#### Podman-Specific Features via Driver Layer

Access Podman-only capabilities through `SysCtl<T>` or `TrySysCtl<T>`:

```csharp
var context = new DriverContext("podman");

// Pods
var pods = kernel.SysCtl<IPodmanPodDriver>("podman");
await pods.CreatePodAsync(context, new PodCreateConfig { Name = "my-pod" });

// Kubernetes
var kube = kernel.SysCtl<IPodmanKubernetesDriver>("podman");
await kube.PlayAsync(context,
    new KubePlayConfig { YamlPath = "/path/to/pod.yaml" });

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

Use `TrySysCtl<T>` to write code that adapts to the active driver:

```csharp
// Works with any driver, uses pods only when available
if (kernel.TrySysCtl<IPodmanPodDriver>(driverId, out var podDriver))
{
    await podDriver.CreatePodAsync(context,
        new PodCreateConfig { Name = "my-pod" });
}

// Generic WithinDriver() for portable code
await using var results = await new Builder()
    .WithinDriver(driverId, kernel)
    .UseContainer(c => c
        .UseImage("alpine:latest"))
    .BuildAsync();
```

---

## Test Support

### MsTest

```csharp
using FluentDocker.MsTest;
using FluentDocker.Builders;

[TestClass]
public class MyTests : FluentDockerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .ExposePort("5432")
            .WaitForPort("5432/tcp", 30000);
    }

    [TestMethod]
    public void Test()
    {
        // Container available via Container property
    }
}
```

### XUnit

```csharp
using FluentDocker.XUnit;
using FluentDocker.Builders;

public class MyFixture : FluentDockerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .ExposePort("5432")
            .WaitForPort("5432/tcp", 30000);
    }
}

public class MyTests : IClassFixture<MyFixture>
{
    private readonly MyFixture _fixture;

    public MyTests(MyFixture fixture) => _fixture = fixture;

    [Fact]
    public void Test()
    {
        // _fixture.Container available
    }
}
```

---

## Utilities

### TemplateString

```csharp
var path = new TemplateString("${TEMP}/${RND}/config");
// Expands to: /tmp/a1b2c3d4/config

var envPath = new TemplateString("${E_HOME}/app");
// Expands to: /home/user/app
```

### Wget Helper

```csharp
var body = await "http://localhost:8080/health".WgetAsync();
var (status, body) = await "http://localhost/api".WgetWithStatusAsync();
```

---

## Linux Users

Docker requires sudo by default. Configure it per-driver:

```csharp
// Via typed kernel builder
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d
        .WithSudo(SudoMechanism.NoPassword)
        .AsDefault())
    .Build();
```

Or add user to docker group: `sudo usermod -aG docker $USER`

---

## Logging

```csharp
Logging.Enabled();   // Enable logging
Logging.Disabled();  // Disable logging
```

Configure via `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "FluentDocker": "Debug"
    }
  }
}
```

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
