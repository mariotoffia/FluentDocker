---
layout: default
title: Docker Compose
nav_order: 5
---

# Docker Compose

FluentDocker provides full support for Docker Compose V2 (`docker compose` command).

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> from [`master`](https://github.com/mariotoffia/FluentDocker/tree/master) to use it. The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## Step by Step

- Basics: [Kernel Setup](#kernel-setup), [Basic Usage](#basic-usage), [Waiting for Services](#waiting-for-services)
- Intermediate: [Project Configuration](#project-configuration), [Multiple Compose Files](#multiple-compose-files), [Access Compose Services](#access-compose-services), [Environment Variables](#environment-variables)
- Advanced: [Profiles](#profiles), [Target Specific Services](#target-specific-services), [Integration Tests Example](#integration-tests-example), [Cleanup Options](#cleanup-options)

## Kernel Setup

Before using the builder, create a `FluentDockerKernel`. Multiple kernels per
application (or test fixture) are supported.

```csharp
using FluentDocker.Kernel;
using FluentDocker.Builders;

// Create once and reuse across builder calls
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

The kernel owns its driver instances, so dispose it when the app or fixture shuts down:
`await using var kernel = ...` for a scoped lifetime, or hold the reference and call
`await kernel.DisposeAsync()` (sync `Dispose()` is the fallback) on shutdown. A synchronous
`Build()` wrapper exists for code that cannot be async.

## Basic Usage

### Start Services

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml"))
    .BuildAsync();

// Services are started during BuildAsync() -- no separate Start() call.
var compose = results.ComposeServices.First();
Console.WriteLine($"Project: {compose.ProjectName}");
```

### Example docker-compose.yml

```yaml
services:
  web:
    image: nginx:alpine
    ports:
      - "80"
    depends_on: [api]
  api:
    image: myapi:latest
    ports:
      - "8080"
    environment:
      - DATABASE_URL=postgres://db:5432/mydb
    depends_on: [db]
  db:
    image: postgres:15-alpine
    environment:
      - POSTGRES_PASSWORD=secret
    volumes:
      - db_data:/var/lib/postgresql/data
volumes:
  db_data:
```

## Waiting for Services

The v3 API uses Docker Compose V2's native `--wait` flag instead of per-service wait
strategies. With `--wait`, Compose waits for every service that has a `healthcheck`
defined in the compose file to report healthy before returning.

### Wait for Healthy Services

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait())
    .BuildAsync();
```

### Wait with Timeout

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait()
        .WithWaitTimeout(120))  // seconds
    .BuildAsync();
```

For `--wait` to be effective, define healthchecks in your compose file:

```yaml
services:
  db:
    image: postgres:15-alpine
    environment:
      POSTGRES_PASSWORD: secret
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      timeout: 5s
      retries: 5

  api:
    image: myapi:latest
    ports:
      - "8080"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 10s
      timeout: 5s
      retries: 3
    depends_on:
      db:
        condition: service_healthy
```

> **Note**: Compose builds return `IComposeService` handles. Use
> `results.ComposeServices.First().ListServicesAsync()` for service state and published
> host ports; `BuildResults.Containers` is only for `UseContainer(...)` builds.

## Project Configuration

### Custom Project Name

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithProjectName("my-test-project"))
    .BuildAsync();

// Containers named: my-test-project-web-1, my-test-project-api-1, etc.
// Without WithProjectName, compose derives the name from the compose-file directory;
// FluentDocker then identifies the project via the files, so teardown still works.
```

### Remove Orphans

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans())  // Remove containers not in compose file
    .BuildAsync();
```

### Force Recreate

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithForceRecreate())  // Recreate even if unchanged
    .BuildAsync();
```

## Multiple Compose Files

### Override Files

```csharp
// Base + override pattern
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFiles(
            "docker-compose.yml",
            "docker-compose.override.yml"))
    .BuildAsync();
```

### Environment-Specific

```csharp
// Development environment
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFiles(
            "docker-compose.yml",
            "docker-compose.dev.yml"))
    .BuildAsync();
```

```yaml
# docker-compose.dev.yml
services:
  web:
    volumes:
      - ./src:/app/src  # Hot reload
    environment:
      - DEBUG=true
```

## Access Compose Services

### Get Services and Published Ports

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait())
    .BuildAsync();

var compose = results.ComposeServices.First();
var services = await compose.ListServicesAsync();

var web = services.FirstOrDefault(s => s.Name == "web");
var api = services.FirstOrDefault(s => s.Name == "api");
Console.WriteLine($"web: {web?.State}, api: {api?.State}");

var webPort = web?.Publishers.FirstOrDefault(p => p.TargetPort == 80)?.PublishedPort;
var apiPort = api?.Publishers.FirstOrDefault(p => p.TargetPort == 8080)?.PublishedPort;
```

### Execute Commands

```csharp
var compose = results.ComposeServices.First();

// Execute in a compose service
var output = await compose.ExecuteAsync(
    "db",
    new[] { "psql", "-U", "postgres", "-c",
            "CREATE TABLE IF NOT EXISTS users (id SERIAL PRIMARY KEY);" });
```

## WordPress with MySQL Example

### docker-compose.yml

```yaml
services:
  db:
    image: mariadb:10.6
    environment:
      MARIADB_ROOT_PASSWORD: rootpassword
      MARIADB_DATABASE: wordpress
      MARIADB_USER: wordpress
      MARIADB_PASSWORD: wordpress
    volumes:
      - db_data:/var/lib/mysql
    healthcheck:
      test: ["CMD", "healthcheck.sh", "--connect", "--innodb_initialized"]
      interval: 10s
      timeout: 5s
      retries: 5

  wordpress:
    image: wordpress:latest
    depends_on:
      db:
        condition: service_healthy
    ports:
      - "80"
    environment:
      WORDPRESS_DB_HOST: db:3306
      WORDPRESS_DB_USER: wordpress
      WORDPRESS_DB_PASSWORD: wordpress
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:80"]
      interval: 15s
      timeout: 5s
      retries: 5

volumes:
  db_data:
```

### C# Code

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait()
        .WithWaitTimeout(120))
    .BuildAsync();

var wordpress = (await results.ComposeServices.First().ListServicesAsync())
    .First(s => s.Name == "wordpress");
var port = wordpress.Publishers.First(p => p.TargetPort == 80).PublishedPort;
Console.WriteLine($"WordPress: http://localhost:{port}");
```

## Kafka with Zookeeper Example

### docker-compose.yml

```yaml
services:
  zookeeper:
    image: confluentinc/cp-zookeeper:7.4.0
    environment:
      ZOOKEEPER_CLIENT_PORT: 2181
      ZOOKEEPER_TICK_TIME: 2000
    healthcheck:
      test: ["CMD", "nc", "-z", "localhost", "2181"]
      interval: 10s
      timeout: 5s
      retries: 5

  kafka:
    image: confluentinc/cp-kafka:7.4.0
    depends_on:
      zookeeper:
        condition: service_healthy
    ports:
      - "9092"
    environment:
      KAFKA_BROKER_ID: 1
      KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:29092,PLAINTEXT_HOST://localhost:9092
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT
      KAFKA_INTER_BROKER_LISTENER_NAME: PLAINTEXT
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
    healthcheck:
      test: ["CMD", "kafka-broker-api-versions", "--bootstrap-server", "localhost:9092"]
      interval: 15s
      timeout: 10s
      retries: 5
```

### C# Code

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait()
        .WithWaitTimeout(90))
    .BuildAsync();

var kafka = (await results.ComposeServices.First().ListServicesAsync())
    .First(s => s.Name == "kafka");
var port = kafka.Publishers.First(p => p.TargetPort == 9092).PublishedPort;
var bootstrapServers = $"localhost:{port}";

Console.WriteLine($"Kafka: {bootstrapServers}");
```

## RabbitMQ Example

### docker-compose.yml

```yaml
services:
  rabbitmq:
    image: rabbitmq:3-management-alpine
    ports:
      - "5672"   # AMQP
      - "15672"  # Management UI
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "check_running"]
      interval: 10s
      timeout: 5s
      retries: 5
```

### C# Code

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait()
        .WithWaitTimeout(60))
    .BuildAsync();

var rabbit = (await results.ComposeServices.First().ListServicesAsync())
    .First(s => s.Name == "rabbitmq");
var amqpPort = rabbit.Publishers.First(p => p.TargetPort == 5672).PublishedPort;
var mgmtPort = rabbit.Publishers.First(p => p.TargetPort == 15672).PublishedPort;
Console.WriteLine($"AMQP: localhost:{amqpPort}");
Console.WriteLine($"Management: http://localhost:{mgmtPort}");
```

## Build Services

### Build Images

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithBuild())  // Build images before starting
    .BuildAsync();
```

> **Note**: For a no-cache rebuild, run `docker compose build --no-cache` separately
> before the builder call, or combine `.WithBuild()` with `.WithForceRecreate()`.

## Environment Variables

### Inline Environment

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithEnvironment("DB_PASSWORD", "secret")
        .WithEnvironment("API_KEY", "abc123"))
    .BuildAsync();
```

### Bulk Environment

```csharp
var env = new Dictionary<string, string>
{
    ["DB_PASSWORD"] = "secret",
    ["API_KEY"] = "abc123"
};

await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithEnvironment(env))
    .BuildAsync();
```

### With .env File

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithEnvFile(".env"))
    .BuildAsync();
```

```yaml
# docker-compose.yml
services:
  db:
    image: postgres:15-alpine
    environment:
      POSTGRES_PASSWORD: ${DB_PASSWORD}
```

```bash
# .env file in same directory
DB_PASSWORD=mysecret
```

## Scaling Services

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithScale("worker", 3))  // Run 3 worker instances
    .BuildAsync();

var workers = (await results.ComposeServices.First().ListServicesAsync())
    .Where(s => s.Name == "worker")
    .ToList();

Console.WriteLine($"Workers: {workers.Count}");  // 3
```

## Cleanup Options

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveVolumes()   // Remove volumes when disposed
        .WithRemoveImages())   // Remove images when disposed
    .BuildAsync();
```

By default, compose services are torn down on dispose. Use `.WithRemoveVolumes()`
and `.WithRemoveImages()` to also remove volumes and images during teardown.
When `.ConnectToExisting()` is used, the returned compose service is borrowed:
dispose releases local resources only and never runs `docker compose down`.

## Additional Builder Methods

The compose builder also supports these options:

- `WithTimeout(int seconds)` -- sets a timeout (in seconds) for the compose operation.
- `WithNoStart(bool)` -- runs `docker compose up` without starting services (create only).
- `WithPull(bool)` -- pulls images before starting services.

## Profiles

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithProfiles("debug", "monitoring"))
    .BuildAsync();
```

## Target Specific Services

```csharp
await using var results = await new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .ForServices("web", "api")  // Only start web and api
        .WithNoDeps())              // Skip their dependencies
    .BuildAsync();
```

## Integration Tests Example

```csharp
using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using Xunit;
public class IntegrationTestBase : IAsyncLifetime
{
  private FluentDockerKernel _kernel = null!;
  protected BuildResults Results { get; private set; } = null!;
  protected string ApiBaseUrl { get; private set; } = "";
  public async ValueTask InitializeAsync()
  {
    _kernel = await FluentDockerKernel.Create()
      .WithDockerCli("docker", d => d.AsDefault())
      .BuildAsync();
    Results = await new Builder()
      .WithinDriver("docker", _kernel)
      .UseCompose(c => c
        .WithComposeFile("docker-compose.test.yml")
        .WithRemoveOrphans()
        .WithWait()
        .WithWaitTimeout(60))
      .BuildAsync();
    var api = (await Results.ComposeServices.First().ListServicesAsync())
      .First(s => s.Name == "api");
    var port = api.Publishers.First(p => p.TargetPort == 8080).PublishedPort;
    ApiBaseUrl = $"http://localhost:{port}";
  }
  public async ValueTask DisposeAsync()
  {
    await Results.DisposeAsync();
    await _kernel.DisposeAsync();
  }
}
public class UserApiTests : IntegrationTestBase
{
  [Fact]
  public async Task CreateUser_ReturnsCreated()
  {
    var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(ApiBaseUrl) };
    var response = await client.PostAsJsonAsync("/users", new { name = "Test" });
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
  }
}
```
