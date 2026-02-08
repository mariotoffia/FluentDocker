---
layout: default
title: Images
nav_order: 7
---

# Image Building

FluentDocker v3 provides a lambda-based API for building Docker images from Dockerfiles
or inline definitions. All builder operations require a kernel and a driver scope.

## Kernel Setup

Before using the builder, create a kernel once per application:

```csharp
using FluentDocker.Kernel;
using FluentDocker.Builders;

// Create kernel (typically once per app or test fixture)
var kernel = FluentDockerKernel.Create()
    .WithDriver("docker", d => d.UseDockerCli().AsDefault())
    .Build();
```

The kernel manages driver lifecycle and is reused across all builder calls.

## Build from Dockerfile

### Basic Build

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", img => img
        .FromFile("/path/to/Dockerfile"))
    .Build();

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

var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", img => img
        .FromString(dockerfileContent))
    .Build();
```

## Inline Dockerfile

### Simple Application

```csharp
var results = new Builder()
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
    .Build();
```

### With Environment Variables

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapi:latest", img => img
        .From("node:18-alpine")
        .Environment("NODE_ENV=production")
        .Environment("PORT=8080")
        .UseWorkDir("/app")
        .Copy("package*.json", "./")
        .Run("npm ci --only=production")
        .Copy(".", ".")
        .ExposePorts(8080)
        .Command("node", "server.js"))
    .Build();
```

### Multi-Stage Build

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", img => img
        // Build stage
        .From("node:18-alpine", "builder")
        .UseWorkDir("/app")
        .Copy("package*.json", "./")
        .Run("npm ci")
        .Copy(".", ".")
        .Run("npm run build")
        // Production stage
        .From("nginx:alpine")
        .Copy("/app/dist", "/usr/share/nginx/html", fromAlias: "builder")
        .ExposePorts(80))
    .Build();
```

Note: For multi-stage COPY --from, use the `fromAlias` parameter on the `Copy` method.

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

```csharp
.Copy("src/", "/app/src/")
.Copy("package.json", "/app/")
.Copy("src/", "/app/src/", chownUserAndGroup: "node:node")
.Copy("/app/dist", "/usr/share/nginx/html", fromAlias: "builder")  // COPY --from
.Add("https://example.com/file.tar.gz", "/app/")  // ADD can fetch URLs
```

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

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapi:latest", img => img
        // Build stage
        .From("mcr.microsoft.com/dotnet/sdk:8.0", "build")
        .UseWorkDir("/src")
        .Copy("*.csproj", "./")
        .Run("dotnet restore")
        .Copy(".", ".")
        .Run("dotnet publish -c Release -o /app/publish")
        // Runtime stage
        .From("mcr.microsoft.com/dotnet/aspnet:8.0")
        .UseWorkDir("/app")
        .Copy("/app/publish", ".", fromAlias: "build")
        .Environment("ASPNETCORE_URLS=http://+:8080")
        .ExposePorts(8080)
        .Entrypoint("dotnet", "MyApi.dll"))
    .Build();
```

### .NET Worker Service

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myworker:latest", img => img
        .From("mcr.microsoft.com/dotnet/sdk:8.0", "build")
        .UseWorkDir("/src")
        .Copy(".", ".")
        .Run("dotnet publish -c Release -o /app")
        .From("mcr.microsoft.com/dotnet/runtime:8.0")
        .UseWorkDir("/app")
        .Copy("/app", ".", fromAlias: "build")
        .Entrypoint("dotnet", "MyWorker.dll"))
    .Build();
```

## Python Application

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myflask:latest", img => img
        .From("python:3.11-slim")
        .UseWorkDir("/app")
        .Copy("requirements.txt", ".")
        .Run("pip install --no-cache-dir -r requirements.txt")
        .Copy(".", ".")
        .Environment("FLASK_APP=app.py")
        .ExposePorts(5000)
        .Command("flask", "run", "--host=0.0.0.0"))
    .Build();
```

## Go Application

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("mygo:latest", img => img
        // Build stage
        .From("golang:1.21-alpine", "builder")
        .UseWorkDir("/app")
        .Copy("go.mod", "./")
        .Copy("go.sum", "./")
        .Run("go mod download")
        .Copy(".", ".")
        .Run("CGO_ENABLED=0 go build -o main .")
        // Runtime stage
        .From("alpine:latest")
        .Run("apk --no-cache add ca-certificates")
        .UseWorkDir("/root/")
        .Copy("/app/main", ".", fromAlias: "builder")
        .ExposePorts(8080)
        .Command("./main"))
    .Build();
```

## Build with Container

Build an image and immediately run it as a container in the same builder chain:

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:test", img => img
        .From("node:18-alpine")
        .UseWorkDir("/app")
        .Copy(".", ".")
        .Run("npm install")
        .ExposePorts(3000)
        .Command("npm", "start"))
    .UseContainer(c => c
        .UseImage("myapp:test")
        .ExposePort(3000, 3000)
        .WaitForPort("3000/tcp", 30000))
    .Build();

// Access the running container from results
var container = results.Containers.First();
```

## Build Arguments in Dockerfile

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", img => img
        .Arguments("VERSION", "1.0.0")
        .Arguments("BUILD_DATE")
        .From("node:18-alpine")
        .Label("version=${VERSION}")
        .Label("build-date=${BUILD_DATE}")
        .UseWorkDir("/app")
        .Copy(".", "."))
    .Build();
```

## Accessing Build Results

The `Build()` and `BuildAsync()` methods return a `BuildResults` object:

```csharp
var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseImage("myapp:latest", img => img.From("alpine:latest"))
    .UseContainer(c => c.UseImage("myapp:latest"))
    .Build();

// All services
var allServices = results.All;

// Typed access
var images = results.OfType<IImageService>();
var containers = results.Containers;

// By name
var myContainer = results.GetContainer("myapp");

// Dispose all services when done
results.Dispose();
```

### Async Build

For async contexts (ASP.NET, UI applications), use `BuildAsync` to avoid deadlocks:

```csharp
var results = await new Builder()
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
public class CustomImageTest : IDisposable
{
    private readonly FluentDockerKernel _kernel;
    private readonly BuildResults _results;

    public CustomImageTest()
    {
        _kernel = FluentDockerKernel.Create()
            .WithDriver("docker", d => d.UseDockerCli().AsDefault())
            .Build();

        _results = new Builder()
            .WithinDriver("docker", _kernel)
            .UseImage("test-app:latest", img => img
                .From("node:18-alpine")
                .UseWorkDir("/app")
                .Copy("./test-fixtures/", "/app/")
                .Run("npm install")
                .ExposePorts(3000)
                .Command("npm", "test"))
            .UseContainer(c => c
                .UseImage("test-app:latest")
                .ExposePort(3000, 3000)
                .WaitForPort("3000/tcp", 30000))
            .Build();
    }

    [Fact]
    public async Task App_ReturnsHealthy()
    {
        var container = _results.Containers.First();
        var endpoint = container.ToHostExposedEndpoint("3000/tcp");
        var response = await $"http://localhost:{endpoint.Port}/health".WgetAsync();
        Assert.Contains("healthy", response);
    }

    public void Dispose()
    {
        _results?.Dispose();
        _kernel?.Dispose();
    }
}
```

## Next Steps

- [Containers](containers.html) - Using built images with containers
- [Docker Compose](compose.html) - Multi-container orchestration
- [Testing](testing.html) - Test fixtures and base classes
