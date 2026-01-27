---
layout: default
title: Images
nav_order: 7
---

# Image Building

FluentDocker provides powerful APIs for building Docker images from Dockerfiles or inline definitions.

## Build from Dockerfile

### Basic Build

```csharp
using FluentDocker.Builders;

using var image = new Builder()
    .DefineImage("myapp:latest")
    .FromFile("/path/to/Dockerfile")
    .Build();

Console.WriteLine($"Image: {image.Name}");
```

### With Build Context

```csharp
using var image = new Builder()
    .DefineImage("myapp:latest")
    .FromFile("/path/to/project/Dockerfile")
    .WorkingFolder("/path/to/project")  // Build context
    .Build();
```

### Reuse Existing Image

```csharp
using var image = new Builder()
    .DefineImage("myapp:latest")
    .FromFile("/path/to/Dockerfile")
    .ReuseIfAlreadyExists()  // Don't rebuild if exists
    .Build();
```

## Inline Dockerfile

### Simple Application

```csharp
using var image = new Builder()
    .DefineImage("mynode:latest")
    .From("node:18-alpine")
    .Run("npm install -g nodemon")
    .Add("app.js", "/app/app.js")
    .Add("package.json", "/app/package.json")
    .UseWorkDir("/app")
    .Run("npm install")
    .ExposePorts(3000)
    .Command("node", "app.js")
    .Build();
```

### With Environment Variables

```csharp
using var image = new Builder()
    .DefineImage("myapi:latest")
    .From("node:18-alpine")
    .Environment("NODE_ENV=production")
    .Environment("PORT=8080")
    .UseWorkDir("/app")
    .Copy("package*.json", "./")
    .Run("npm ci --only=production")
    .Copy(".", ".")
    .ExposePorts(8080)
    .Command("node", "server.js")
    .Build();
```

### Multi-Stage Build

```csharp
using var image = new Builder()
    .DefineImage("myapp:latest")
    // Build stage
    .From("node:18-alpine", "builder")
    .UseWorkDir("/app")
    .Copy("package*.json", "./")
    .Run("npm ci")
    .Copy(".", ".")
    .Run("npm run build")
    // Production stage
    .From("nginx:alpine")
    .CopyFrom("builder", "/app/dist", "/usr/share/nginx/html")
    .ExposePorts(80)
    .Build();
```

## Dockerfile Instructions

### FROM

```csharp
.From("node:18-alpine")
.From("node:18-alpine", "builder")  // Named stage
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
.User("1000:1000")  // UID:GID
```

### VOLUME

```csharp
.Volume("/data")
.Volume("/data", "/logs", "/config")
```

### LABEL

```csharp
.Label("version", "1.0.0")
.Label("maintainer", "dev@example.com")
```

### ARG

```csharp
.Arg("VERSION", "1.0.0")
.Arg("NODE_VERSION", "18")
```

### HEALTHCHECK

```csharp
.HealthCheck("curl -f http://localhost/ || exit 1",
    interval: TimeSpan.FromSeconds(30),
    timeout: TimeSpan.FromSeconds(10),
    retries: 3)
```

## .NET Application Examples

### ASP.NET Core API

```csharp
using var image = new Builder()
    .DefineImage("myapi:latest")
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
    .CopyFrom("build", "/app/publish", ".")
    .Environment("ASPNETCORE_URLS=http://+:8080")
    .ExposePorts(8080)
    .Entrypoint("dotnet", "MyApi.dll")
    .Build();
```

### .NET Worker Service

```csharp
using var image = new Builder()
    .DefineImage("myworker:latest")
    .From("mcr.microsoft.com/dotnet/sdk:8.0", "build")
    .UseWorkDir("/src")
    .Copy(".", ".")
    .Run("dotnet publish -c Release -o /app")
    .From("mcr.microsoft.com/dotnet/runtime:8.0")
    .UseWorkDir("/app")
    .CopyFrom("build", "/app", ".")
    .Entrypoint("dotnet", "MyWorker.dll")
    .Build();
```

## Python Application

```csharp
using var image = new Builder()
    .DefineImage("myflask:latest")
    .From("python:3.11-slim")
    .UseWorkDir("/app")
    .Copy("requirements.txt", ".")
    .Run("pip install --no-cache-dir -r requirements.txt")
    .Copy(".", ".")
    .Environment("FLASK_APP=app.py")
    .ExposePorts(5000)
    .Command("flask", "run", "--host=0.0.0.0")
    .Build();
```

## Go Application

```csharp
using var image = new Builder()
    .DefineImage("mygo:latest")
    // Build stage
    .From("golang:1.21-alpine", "builder")
    .UseWorkDir("/app")
    .Copy("go.mod", "go.sum", "./")
    .Run("go mod download")
    .Copy(".", ".")
    .Run("CGO_ENABLED=0 go build -o main .")
    // Runtime stage
    .From("alpine:latest")
    .Run("apk --no-cache add ca-certificates")
    .UseWorkDir("/root/")
    .CopyFrom("builder", "/app/main", ".")
    .ExposePorts(8080)
    .Command("./main")
    .Build();
```

## Build with Container

Build an image and immediately use it:

```csharp
using var services = new Builder()
    .DefineImage("myapp:test")
    .From("node:18-alpine")
    .UseWorkDir("/app")
    .Copy(".", ".")
    .Run("npm install")
    .ExposePorts(3000)
    .Command("npm", "start")
    .ReuseIfAlreadyExists()
    .Builder()  // Return to main builder
    .UseContainer()
    .UseImage("myapp:test")
    .ExposePort(3000)
    .WaitForPort("3000/tcp", 30000)
    .Build()
    .Start();
```

## Build Arguments

```csharp
using var image = new Builder()
    .DefineImage("myapp:latest")
    .Arg("VERSION", "1.0.0")
    .Arg("BUILD_DATE")
    .From("node:18-alpine")
    .Label("version", "${VERSION}")
    .Label("build-date", "${BUILD_DATE}")
    .UseWorkDir("/app")
    .Copy(".", ".")
    .Build();

// Pass build args at build time
image.BuildWithArgs(new Dictionary<string, string>
{
    ["VERSION"] = "2.0.0",
    ["BUILD_DATE"] = DateTime.UtcNow.ToString("o")
});
```

## Image Operations

### Pull Image

```csharp
// Pull if not exists
using var container = new Builder()
    .UseContainer()
    .UseImage("postgres:15-alpine")  // Auto-pulls if needed
    .Build()
    .Start();
```

### List Images

```csharp
var images = await dockerHost.ImagesAsync();
foreach (var img in images)
{
    Console.WriteLine($"{img.RepoTags?.FirstOrDefault()} - {img.Size} bytes");
}
```

### Remove Image

```csharp
using var image = new Builder()
    .DefineImage("temp-image:latest")
    .From("alpine:latest")
    .Run("echo 'hello'")
    .Build();

// Use image...

// Remove when done
image.Remove();
```

### Tag Image

```csharp
using var image = new Builder()
    .DefineImage("myapp:latest")
    .FromFile("/path/to/Dockerfile")
    .Build();

// Tag for registry
image.Tag("registry.example.com/myapp:v1.0.0");
image.Tag("registry.example.com/myapp:latest");
```

### Push Image

```csharp
using var image = new Builder()
    .DefineImage("registry.example.com/myapp:v1.0.0")
    .FromFile("/path/to/Dockerfile")
    .Build();

// Push to registry
await image.PushAsync();
```

## Build Cache

### No Cache

```csharp
using var image = new Builder()
    .DefineImage("myapp:latest")
    .FromFile("/path/to/Dockerfile")
    .NoCache()  // Rebuild all layers
    .Build();
```

### Pull Latest Base

```csharp
using var image = new Builder()
    .DefineImage("myapp:latest")
    .FromFile("/path/to/Dockerfile")
    .Pull()  // Pull latest base image
    .Build();
```

## Resource Files

### Embed Resources in Image

```csharp
// Extract embedded resources first
var configPath = ResourceQuery.ExtractToTemp("MyApp.Resources.config.json");
var scriptsPath = ResourceQuery.ExtractToTemp("MyApp.Resources.scripts/");

using var image = new Builder()
    .DefineImage("myapp:latest")
    .From("alpine:latest")
    .Copy(configPath, "/app/config.json")
    .Copy(scriptsPath, "/app/scripts/")
    .Build();
```

## Testing with Custom Images

```csharp
public class CustomImageTest : IDisposable
{
    private readonly IImageService _image;
    private readonly IContainerService _container;

    public CustomImageTest()
    {
        // Build test image with mock dependencies
        _image = new Builder()
            .DefineImage("test-app:latest")
            .From("node:18-alpine")
            .UseWorkDir("/app")
            .Copy("./test-fixtures/", "/app/")
            .Run("npm install")
            .ExposePorts(3000)
            .Command("npm", "test")
            .Build();

        _container = new Builder()
            .UseContainer()
            .UseImage("test-app:latest")
            .ExposePort(3000)
            .WaitForPort("3000/tcp", 30000)
            .Build()
            .Start();
    }

    [Fact]
    public async Task App_ReturnsHealthy()
    {
        var endpoint = _container.ToHostExposedEndpoint("3000/tcp");
        var response = await $"http://localhost:{endpoint.Port}/health".WgetAsync();
        Assert.Contains("healthy", response);
    }

    public void Dispose()
    {
        _container?.Dispose();
        _image?.Remove();
    }
}
```

## Next Steps

- [Containers](containers.html) - Using built images
- [Docker Compose](compose.html) - Multi-container builds
- [Testing](testing.html) - Test fixtures and base classes
