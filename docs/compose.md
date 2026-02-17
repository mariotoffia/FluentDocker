---
layout: default
title: Docker Compose
nav_order: 5
---

# Docker Compose

FluentDocker provides full support for Docker Compose V2 (`docker compose` command).

## Step by Step

- Basics: [Kernel Setup](#kernel-setup), [Basic Usage](#basic-usage), [Waiting for Services](#waiting-for-services)
- Intermediate: [Project Configuration](#project-configuration), [Multiple Compose Files](#multiple-compose-files), [Access Containers](#access-containers), [Environment Variables](#environment-variables)
- Advanced: [Profiles](#profiles), [Target Specific Services](#target-specific-services), [Integration Tests Example](#integration-tests-example), [Cleanup Options](#cleanup-options)

## Kernel Setup

Before using the builder, create a `FluentDockerKernel`. Multiple kernels per
application (or test fixture) are supported. Many apps still reuse one kernel
across builder calls for simplicity.

```csharp
using FluentDocker.Kernel;
using FluentDocker.Builders;

// Create once and reuse
var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();
```

For async contexts (ASP.NET, xUnit `IAsyncLifetime`), prefer the async variant:

```csharp
var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();
```

## Basic Usage

### Start Services

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml"))
    .Build();

// Services are started during Build() -- no separate Start() call.
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
    depends_on:
      - api

  api:
    image: myapi:latest
    ports:
      - "8080"
    environment:
      - DATABASE_URL=postgres://db:5432/mydb
    depends_on:
      - db

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
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait())
    .Build();
```

### Wait with Timeout

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait()
        .WithWaitTimeout(120))  // seconds
    .Build();
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

> **Note**: If you need fine-grained wait logic (HTTP polling with custom validation,
> port probing, etc.) after compose services are up, you can access individual
> containers from `results.Containers` and use the container-level wait utilities.

## Project Configuration

### Custom Project Name

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithProjectName("my-test-project"))
    .Build();

// Containers named: my-test-project-web-1, my-test-project-api-1, etc.
```

### Remove Orphans

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveOrphans())  // Remove containers not in compose file
    .Build();
```

### Force Recreate

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithForceRecreate())  // Recreate even if unchanged
    .Build();
```

## Multiple Compose Files

### Override Files

```csharp
// Base + override pattern
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFiles(
            "docker-compose.yml",
            "docker-compose.override.yml"))
    .Build();
```

### Environment-Specific

```csharp
// Development environment
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFiles(
            "docker-compose.yml",
            "docker-compose.dev.yml"))
    .Build();
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

## Access Containers

### Get Specific Container

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait())
    .Build();

// Find by name
var webContainer = results.Containers
    .FirstOrDefault(c => c.Name.Contains("web"));

var apiContainer = results.Containers
    .FirstOrDefault(c => c.Name.Contains("api"));

// Get endpoints
var webEndpoint = webContainer?.ToHostExposedEndpoint("80/tcp");
var apiEndpoint = apiContainer?.ToHostExposedEndpoint("8080/tcp");
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
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait()
        .WithWaitTimeout(120))
    .Build();

var wpContainer = results.Containers
    .First(c => c.Name.Contains("wordpress"));

var endpoint = wpContainer.ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"WordPress: http://localhost:{endpoint.Port}");
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
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait()
        .WithWaitTimeout(90))
    .Build();

var kafkaContainer = results.Containers
    .First(c => c.Name.Contains("kafka"));

var endpoint = kafkaContainer.ToHostExposedEndpoint("9092/tcp");
var bootstrapServers = $"localhost:{endpoint.Port}";

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
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithWait()
        .WithWaitTimeout(60))
    .Build();

var rmqContainer = results.Containers
    .First(c => c.Name.Contains("rabbitmq"));

var amqpEndpoint = rmqContainer.ToHostExposedEndpoint("5672/tcp");
var mgmtEndpoint = rmqContainer.ToHostExposedEndpoint("15672/tcp");

Console.WriteLine($"AMQP: amqp://guest:guest@localhost:{amqpEndpoint.Port}");
Console.WriteLine($"Management: http://localhost:{mgmtEndpoint.Port}");
```

## Build Services

### Build Images

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithBuild())  // Build images before starting
    .Build();
```

> **Note**: For a no-cache rebuild, run `docker compose build --no-cache` separately
> before the builder call, or combine `.WithBuild()` with `.WithForceRecreate()`.

## Environment Variables

### Inline Environment

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithEnvironment("DB_PASSWORD", "secret")
        .WithEnvironment("API_KEY", "abc123"))
    .Build();
```

### Bulk Environment

```csharp
var env = new Dictionary<string, string>
{
    ["DB_PASSWORD"] = "secret",
    ["API_KEY"] = "abc123"
};

using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithEnvironment(env))
    .Build();
```

### With .env File

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithEnvFile(".env"))
    .Build();
```

```yaml
# docker-compose.yml
services:
  db:
    image: postgres:15-alpine
    environment:
      POSTGRES_PASSWORD: ${DB_PASSWORD}
```

```
# .env file in same directory
DB_PASSWORD=mysecret
```

## Scaling Services

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithScale("worker", 3))  // Run 3 worker instances
    .Build();

var workers = results.Containers
    .Where(c => c.Name.Contains("worker"))
    .ToList();

Console.WriteLine($"Workers: {workers.Count}");  // 3
```

## Cleanup Options

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithRemoveVolumes()   // Remove volumes when disposed
        .WithRemoveImages())   // Remove images when disposed
    .Build();
```

> **Note**: To keep containers after dispose (e.g. for debugging), use
> `.KeepContainer()` on individual container builders via `UseContainer(...)`.
> Compose services are always torn down on dispose.

## Profiles

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithProfiles("debug", "monitoring"))
    .Build();
```

## Target Specific Services

```csharp
using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .ForServices("web", "api")  // Only start web and api
        .WithNoDeps())              // Skip their dependencies
    .Build();
```

## Integration Tests Example

```csharp
public class IntegrationTestBase : IAsyncLifetime
{
    private FluentDockerKernel _kernel;
    protected BuildResults Results { get; private set; }
    protected string ApiBaseUrl { get; private set; }

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

        var apiContainer = Results.Containers
            .First(c => c.Name.Contains("api"));
        var endpoint = apiContainer.ToHostExposedEndpoint("8080/tcp");
        ApiBaseUrl = $"http://localhost:{endpoint.Port}";
    }

    public async ValueTask DisposeAsync()
    {
        if (Results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (_kernel is IAsyncDisposable kd) await kd.DisposeAsync();
    }
}

public class UserApiTests : IntegrationTestBase
{
    [Fact]
    public async Task CreateUser_ReturnsCreated()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(ApiBaseUrl)
        };

        var response = await client.PostAsJsonAsync(
            "/users", new { name = "Test" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
```

## Next Steps
- [Containers](containers.html) - Individual container management
- [Networking](networking.html) - Custom networks
- [Volumes](volumes.html) - Data persistence
