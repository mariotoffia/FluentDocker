# FluentDocker v3.0.0 Tests

This directory contains unit tests for the v3.0.0 driver layer architecture.

## Test Structure

### KernelBuilderTests.cs
Tests for the fluent kernel builder with async/await pattern:
- Single driver registration
- Multiple driver registration
- SysCtl() driver access
- Default driver management

### BuilderTests.cs
Tests for the async builder with WithinDriver() scoping:
- Single scope container creation
- Multi-scope deployments
- Kernel reuse pattern
- BuildResults verification

## Running Tests

Most tests are marked with `Skip` because they require:
1. Docker daemon running
2. Phase 5 implementation (Docker CLI driver)

To run the tests:

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~KernelBuilderTests"

# Run integration tests (requires Docker)
dotnet test --filter "Category=Integration"
```

## Test Categories

- **Unit Tests**: Tests that verify API compilation and basic logic
- **Integration Tests**: Tests marked with `Skip` that require Docker daemon

## v3.0.0 API Examples

### Kernel Creation

```csharp
var kernel = await FluentDockerKernel.Create()
    .WithDriver("docker", d => d.UseDockerCli())
    .BuildAsync();
```

### Multi-Driver Setup

```csharp
var kernel = await FluentDockerKernel.Create()
    .WithDriver("docker-local", d => d
        .UseDockerCli()
        .AtHost("unix:///var/run/docker.sock"))
    .WithDriver("docker-remote", d => d
        .UseDockerCli()
        .AtHost("tcp://remote:2376")
        .WithCertificates("/certs")
        .AsDefault())
    .BuildAsync();
```

### Container Deployment

```csharp
var results = await new Builder()
    .WithinDriver("docker", kernel)
        .UseContainer(c => c
            .UseImage("nginx")
            .WithName("web"))
    .BuildAsync();

await results.All[0].StartAsync();
await results.DisposeAllAsync();
```

### Multi-Scope Deployment

```csharp
var results = await new Builder()
    .WithinDriver("docker-dev", kernel)
        .UseContainer(c => c.UseImage("myapp:dev"))
        .UseContainer(c => c.UseImage("postgres:14"))
    .WithinDriver("docker-prod")  // Reuses kernel
        .UseContainer(c => c.UseImage("myapp:v1.0"))
    .BuildAsync();

var devServices = results.ForDriver("docker-dev");    // [myapp:dev, postgres:14]
var prodServices = results.ForDriver("docker-prod");  // [myapp:v1.0]
```

## Implementation Status

✅ **Phase 1**: Core Infrastructure (Enums, Exceptions, Models, Interfaces)
✅ **Phase 2**: Kernel Infrastructure (Registry, Kernel, Builder)
✅ **Phase 3**: Async Builder (WithinDriver, BuildAsync, BuildResults)
⏳ **Phase 4**: Service Updates (Async service interfaces)
⏳ **Phase 5**: Docker CLI Driver (Full implementation)
⏳ **Phase 6**: Testing (Comprehensive test suite)

## Notes

- All tests use async/await pattern
- CancellationToken support throughout
- IAsyncDisposable for proper cleanup
- Terminal BuildAsync() pattern
