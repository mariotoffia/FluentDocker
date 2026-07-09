---
layout: default
title: Images
nav_order: 8
---

# Image Building

FluentDocker v3 provides a lambda-based API for building Docker images from Dockerfiles
or inline definitions. All builder operations require a kernel and a driver scope.

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> from [`featrure/model-support`](https://github.com/mariotoffia/FluentDocker/tree/featrure/model-support) to use it. The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## Step by Step

- Basics: [Kernel Setup](#kernel-setup), [Build from Dockerfile](#build-from-dockerfile), [Inline Dockerfile](#inline-dockerfile)
- Intermediate: [Dockerfile Instructions](#dockerfile-instructions), [.NET Application Examples](#net-application-examples), [Build Arguments in Dockerfile](#build-arguments-in-dockerfile)
- Advanced: [Build with Container](#build-with-container), [Accessing Build Results](#accessing-build-results), [Testing with Custom Images](#testing-with-custom-images)

## Kernel Setup

Before using the builder, create a kernel.
Multiple kernels per application are supported:

```csharp
using FluentDocker.Kernel;
using FluentDocker.Builders;

// Create kernel (multiple kernels per app are supported)
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

The kernel manages driver lifecycle. Many apps reuse one kernel across builder
calls, but using multiple kernels in the same app is supported.

## Build from Dockerfile

### Basic Build

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", img => img
        .FromFile("/path/to/Dockerfile"))
    .BuildAsync();

var image = results.All.OfType<IImageService>().First();
Console.WriteLine($"Image: {image.Name}");
```

### Build from Dockerfile String

```csharp
var dockerfileContent = @"
FROM node:18-alpine
WORKDIR /app
COPY . .
RUN npm install
CMD [""node"", ""app.js""]
";

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", img => img
        .FromString(dockerfileContent))
    .BuildAsync();
```

## Inline Dockerfile

### Simple Application

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("mynode:latest", img => img
        .From("node:18-alpine")
        .Run("npm install -g nodemon")
        .Add("app.js", "/app/app.js")
        .Add("package.json", "/app/package.json")
        .UseWorkDir("/app")
        .Run("npm install")
        .ExposePorts(3000)
        .Command("node", "app.js"))
    .BuildAsync();
```

**Important**: The `UseImage(name, configure)` lambda receives a `DockerfileBuilder`, not an
`IImageBuilder`. The `DockerfileBuilder` provides Dockerfile instructions (`.From()`, `.Run()`,
`.Copy()`, etc.). The `IImageBuilder` methods listed below are set at a different level -- on the
`ImageBuilder` that wraps the `DockerfileBuilder`. See the [IImageBuilder Methods](#iimagebuilder-methods)
section for details.

### IImageBuilder Methods

The `ImageBuilder` class (which implements `IImageBuilder`) provides build-level configuration
that is separate from the Dockerfile instructions. These methods are not available inside the
`UseImage` lambda directly. Instead, they can be accessed by calling `.ToImage()` on the
`DockerfileBuilder` to return to the `ImageBuilder`:

| Method | Description |
|--------|-------------|
| `ReuseIfAlreadyExists()` | Skip the build if an image with the same name/tag already exists |
| `AsImageName(string name)` | Set or override the image name |
| `ImageTag(params string[] tags)` | Add additional tags to the built image |
| `BuildArguments(params string[] args)` | Pass build arguments (format: `"KEY=VALUE"`) |
| `Label(params string[] labels)` | Add labels to the image metadata (format: `"key=value"`) |
| `NoCache()` | Disable the build cache |
| `AlwaysPull()` | Always pull the base image, even if cached locally |
| `RemoveIntermediate(bool force = false)` | Remove intermediate containers after a successful build |
| `Platform(string platform)` | Set the target platform (e.g., `"linux/amd64"`) |
| `Target(string target)` | Set the target build stage in a multi-stage Dockerfile |

### Copying host files: two patterns

> **Note:** The inline `df => df.Copy(...)` / `df => df.Add(...)` builder stages **individual existing host files** into a temporary build context before invoking Docker. Two restrictions follow from that:
>
> - **Directory sources are not supported** and throw `NotSupportedException("Directory sources are not supported by DockerfileBuilder; add files individually.")`. So `.Copy(".", ".")` and `.Copy("src/", "/app/src/")` throw at build time.
> - **Glob patterns are not expanded or staged.** `.Copy("package*.json", "./")` stages nothing, so the generated `COPY package*.json ./` fails at `docker build` (or throws `COPY source '...' not found` under strict-copy mode).
>
> A multi-stage `COPY --from` via the `fromAlias:` parameter is supported. For anything that needs `COPY . .`, globs, or whole directories, use Pattern B below.

**Pattern A -- inline per-file copy.** Use only for a small, explicit set of existing host files:

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("doc-snippet:latest", df => df
        .UseParent("alpine:3.20")
        .Copy("package.json", "/app/package.json")
        .Copy("Program.cs", "/app/Program.cs"))
    .BuildAsync();
```

**Pattern B -- `FromFile` + `WithBuildContext`.** Use for real projects that need `COPY . .`, globs, directories, or a normal multi-stage build. Write a Dockerfile on disk and point the builder at a build-context directory; Docker resolves the instructions against that context:

```dockerfile
# ./Dockerfile
FROM node:18-alpine
WORKDIR /app
COPY package*.json ./
RUN npm ci --only=production
COPY . .
EXPOSE 8080
CMD ["node", "server.js"]
```

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapi:latest", df => df
        .FromFile("Dockerfile")
        .WithBuildContext("."))
    .BuildAsync();
```

### With Environment Variables

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapi:latest", img => img
        .From("node:18-alpine")
        .Environment("NODE_ENV=production")
        .Environment("PORT=8080")
        .UseWorkDir("/app")
        .Copy("package.json", "/app/package.json")
        .Copy("package-lock.json", "/app/package-lock.json")
        .Run("npm ci --only=production")
        .Copy("server.js", "/app/server.js")
        .ExposePorts(8080)
        .Command("node", "server.js"))
    .BuildAsync();
```

### Multi-Stage Build

Real multi-stage builds copy whole directories and globs, so use Pattern B with a Dockerfile on disk:

```dockerfile
# ./Dockerfile
FROM node:18-alpine AS builder
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=builder /app/dist /usr/share/nginx/html
EXPOSE 80
```

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", df => df
        .FromFile("Dockerfile")
        .WithBuildContext("."))
    .BuildAsync();
```

Note: Docker resolves `COPY --from=builder` against the earlier stage; the build context supplies `COPY . .` and the glob.

## Dockerfile Instructions

### FROM

```csharp
.From("node:18-alpine")
.From("node:18-alpine", "builder")       // Named stage
.From("node:18-alpine", platform: "linux/amd64")  // With platform
```

### RUN

```csharp
.Run("apt-get update && apt-get install -y curl")
.Run("npm install")
```

### COPY and ADD

On the inline builder, `Copy` takes a single existing host file (plus optional `chownUserAndGroup:` and `fromAlias:`):

```csharp
.Copy("package.json", "/app/package.json")                         // single existing host file
.Copy("Program.cs", "/app/", chownUserAndGroup: "node:node")       // single file, with chown
.Copy("/app/dist", "/usr/share/nginx/html", fromAlias: "builder")  // COPY --from (stage to stage)
.Add("https://example.com/file.tar.gz", "/app/")                   // ADD can fetch URLs
```

Directories and globs are not supported here. For `COPY . .`, `COPY src/ ...`, or `COPY *.csproj ...`, use Pattern B (`FromFile(...).WithBuildContext(...)`).

### WORKDIR

```csharp
.UseWorkDir("/app")
```

### ENV

```csharp
.Environment("NODE_ENV=production")
.Environment("PORT=8080", "HOST=0.0.0.0")
```

### EXPOSE

```csharp
.ExposePorts(80)
.ExposePorts(80, 443, 8080)
```

### CMD and ENTRYPOINT

```csharp
.Command("node", "app.js")
.Entrypoint("docker-entrypoint.sh")
```

### USER

```csharp
.User("node")
.User("1000", "1000")  // UID, GID
```

### VOLUME

```csharp
.Volume("/data")
.Volume("/data", "/logs", "/config")
```

### LABEL

```csharp
.Label("version=1.0.0")
.Label("maintainer=dev@example.com")
```

### ARG

```csharp
.Arguments("VERSION", "1.0.0")
.Arguments("NODE_VERSION")
```

### HEALTHCHECK

```csharp
.WithHealthCheck("curl -f http://localhost/ || exit 1",
    interval: "30s",
    timeout: "10s",
    retries: 3)
```

### SHELL

```csharp
.Shell("/bin/bash", "-c")
```

## .NET Application Examples

### ASP.NET Core API

```dockerfile
# ./Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MyApi.dll"]
```

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapi:latest", df => df
        .FromFile("Dockerfile")
        .WithBuildContext("."))
    .BuildAsync();
```

### .NET Worker Service

```dockerfile
# ./Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MyWorker.dll"]
```

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myworker:latest", df => df
        .FromFile("Dockerfile")
        .WithBuildContext("."))
    .BuildAsync();
```

## Python Application

```dockerfile
# ./Dockerfile
FROM python:3.11-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
ENV FLASK_APP=app.py
EXPOSE 5000
CMD ["flask", "run", "--host=0.0.0.0"]
```

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myflask:latest", df => df
        .FromFile("Dockerfile")
        .WithBuildContext("."))
    .BuildAsync();
```

## Go Application

```dockerfile
# ./Dockerfile
FROM golang:1.21-alpine AS builder
WORKDIR /app
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -o main .

FROM alpine:latest
RUN apk --no-cache add ca-certificates
WORKDIR /root/
COPY --from=builder /app/main .
EXPOSE 8080
CMD ["./main"]
```

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("mygo:latest", df => df
        .FromFile("Dockerfile")
        .WithBuildContext("."))
    .BuildAsync();
```

## Build with Container

Build an image and immediately run it as a container in the same builder chain. The image uses Pattern B so `COPY . .` resolves against the build context:

```dockerfile
# ./Dockerfile
FROM node:18-alpine
WORKDIR /app
COPY . .
RUN npm install
EXPOSE 3000
CMD ["npm", "start"]
```

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:test", df => df
        .FromFile("Dockerfile")
        .WithBuildContext("."))
    .UseContainer(c => c
        .UseImage("myapp:test")
        .ExposePort(3000, 3000)
        .WaitForPort("3000/tcp", 30000))
    .BuildAsync();

// Access the running container from results
var container = results.Containers.First();
```

## Build Arguments in Dockerfile

> **Warning:** FluentDocker expands `${...}` tokens on the host, in the .NET process, before the Dockerfile is written or handed to Docker. The recognized tokens are `${TMP}` and `${TEMP}` (host temp dir), `${PWD}` (host working directory), `${RND}` (random file name), and `${E_NAME}` for a host environment variable (for example `${E_PATH}`). Recognized tokens are always expanded; there is no escape syntax for a literal `${TMP}`. Because expansion happens on the host, `.Run("mkdir -p ${TMP}/x")` bakes the *host's* temp path into the image rather than a container path. A `${...}` name that is **not** in this list -- for example `${VERSION}` -- is left untouched and passed to Docker verbatim, so Docker's own build-arg expansion still applies. The catch is Docker's own scoping: an `ARG` is only in scope **after** the `FROM` that consumes it. To label an image with a build arg, declare `ARG VERSION` after `FROM` in a real Dockerfile and reference Docker's `$VERSION` (Pattern B), supplying values through `BuildArguments`:

```dockerfile
# ./Dockerfile
FROM node:18-alpine
ARG VERSION
ARG BUILD_DATE
LABEL version=$VERSION
LABEL build-date=$BUILD_DATE
WORKDIR /app
COPY . .
```

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", df => df
        .FromFile("Dockerfile")
        .WithBuildContext(".")
        .ToImage()
        .BuildArguments("VERSION=1.0.0", "BUILD_DATE=2024-01-01"))
    .BuildAsync();
```

## Accessing Build Results

`BuildAsync()` returns a `BuildResults` object:

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", img => img.From("alpine:latest"))
    .UseContainer(c => c
        .UseImage("myapp:latest")
        .WithName("myapp"))
    .BuildAsync();

// All services
var allServices = results.All;

// Typed access
var images = results.OfType<IImageService>();
var containers = results.Containers;

// By name (requires .WithName("myapp") on the container builder)
var myContainer = results.GetContainer("myapp");

// Disposed asynchronously by await using
```

**Note**: `BuildResults` does not have an `Images` property. To access built images, use the
generic `OfType<T>()` method:

```csharp
var images = results.OfType<IImageService>();
// or equivalently:
var images = results.All.OfType<IImageService>().ToList();
```

The available typed convenience properties on `BuildResults` are: `Containers`, `Networks`,
`Volumes`, and `ComposeServices`. For images, always use `OfType<IImageService>()`.

### Async Build

For async contexts (ASP.NET, UI applications), use `BuildAsync` to avoid deadlocks:

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", img => img
        .From("alpine:latest")
        .Run("echo 'hello'"))
    .BuildAsync();

// Async disposal
await results.DisposeAllAsync();
```

## Testing with Custom Images

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;
using Xunit;

public class CustomImageTest : IAsyncLifetime
{
    private FluentDockerKernel _kernel = null!;
    private BuildResults _results = null!;

    public async ValueTask InitializeAsync()
    {
        _kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        _results = await new Builder()
            .WithinDriver("docker", _kernel)
            .UseImage("test-app:latest", img => img
                .From("node:18-alpine")
                .UseWorkDir("/app")
                .Copy("test-fixtures/package.json", "/app/package.json")
                .Copy("test-fixtures/app.js", "/app/app.js")
                .Run("npm install")
                .ExposePorts(3000)
                .Command("npm", "test"))
            .UseContainer(c => c
                .UseImage("test-app:latest")
                .ExposePort(3000, 3000)
                .WaitForPort("3000/tcp", 30000))
            .BuildAsync();
    }

    [Fact]
    public async Task App_ReturnsHealthy()
    {
        var container = _results.Containers.First();
        var endpoint = await container.ToHostExposedEndpointAsync("3000/tcp");
        using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var response = await FluentDocker.Common.SharedHttpClient.Instance.GetStringAsync(
            $"http://localhost:{endpoint.Port}/health", requestCts.Token);
        Assert.Contains("healthy", response);
    }

    public async ValueTask DisposeAsync()
    {
        await _results.DisposeAsync();
        await _kernel.DisposeAsync();
    }
}
```

## Next Steps

- [Containers](containers.md) - Using built images with containers
- [Docker Compose](compose.md) - Multi-container orchestration
- [Testing](testing.md) - Test fixtures and base classes
