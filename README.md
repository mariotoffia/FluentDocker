# FluentDocker

[![CI](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml/badge.svg)](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/mariotoffia/FluentDocker/branch/master/graph/badge.svg)](https://codecov.io/gh/mariotoffia/FluentDocker)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-net8.0%20%7C%20net10.0-blueviolet)](https://dotnet.microsoft.com/)

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
- **Kernel + WithinDriver() scoping** for multi-driver support
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
    .WithDriver("docker", d => d.UseDockerCli().AsDefault())
    .Build();

// 2. Start a container and wait for it to be ready
using var results = new Builder()
    .WithinDriver("docker", kernel)
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
    .WithinDriver("docker", kernel)
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
    .WithinDriver("docker", kernel)
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
    .WithinDriver("docker", kernel)
    .UseNetwork(n => n
        .WithName("my-network")
        .WithSubnet("10.18.0.0/16"))
    .Build();

var network = nwResults.Networks.First();

using var cResults = new Builder()
    .WithinDriver("docker", kernel)
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
    .WithinDriver("docker", kernel)
    .UseImage("mynode:latest", img => img
        .From("node:18-alpine")
        .Run("npm install -g nodemon")
        .ExposePorts(8080)
        .Command("node", "app.js"))
    .Build();
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

Docker requires sudo by default. Options:

```csharp
SudoMechanism.None.SetSudo();       // Default - no sudo
SudoMechanism.NoPassword.SetSudo(); // Passwordless sudo
SudoMechanism.Password.SetSudo("password");
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
- [Architecture Docs](docs/architecture/) - v3 architecture details
- [NuGet Package](https://www.nuget.org/packages/FluentDocker)

---

## License

Apache 2.0 - See [LICENSE](LICENSE) for details.
