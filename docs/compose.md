---
layout: default
title: Docker Compose
nav_order: 4
---

# Docker Compose

FluentDocker provides full support for Docker Compose V2 (`docker compose` command).

## Basic Usage

### Start Services

```csharp
using FluentDocker.Builders;

using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .Build()
    .Start();

// All services are running
Console.WriteLine($"Services: {services.Containers.Count}");
```

### Example docker-compose.yml

```yaml
version: '3.8'
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

## Wait Strategies

### Wait for HTTP Endpoint

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .WaitForHttp("web", "http://localhost:80/health")
    .Build()
    .Start();
```

### Wait with Custom Validation

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .WaitForHttp("api", "http://localhost:8080/health",
        continuation: (response, attempt) =>
        {
            if (response.Code == HttpStatusCode.OK &&
                response.Body.Contains("\"status\":\"healthy\""))
            {
                return 0;  // Ready
            }
            return 500;  // Retry
        })
    .Build()
    .Start();
```

### Wait for Port

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .WaitForPort("db", "5432/tcp", 30000)
    .Build()
    .Start();
```

### Wait for Multiple Services

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .WaitForPort("db", "5432/tcp", 30000)
    .WaitForPort("redis", "6379/tcp", 30000)
    .WaitForHttp("api", "http://localhost:8080/health")
    .WaitForHttp("web", "http://localhost:80/")
    .Build()
    .Start();
```

## Project Configuration

### Custom Project Name

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .WithProjectName("my-test-project")
    .Build()
    .Start();

// Containers named: my-test-project-web-1, my-test-project-api-1, etc.
```

### Remove Orphans

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .RemoveOrphans()  // Remove containers not in compose file
    .Build()
    .Start();
```

### Force Recreate

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .ForceRecreate()  // Recreate even if unchanged
    .Build()
    .Start();
```

## Multiple Compose Files

### Override Files

```csharp
// Base + override pattern
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .FromFile("docker-compose.override.yml")
    .Build()
    .Start();
```

### Environment-Specific

```csharp
// Development environment
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .FromFile("docker-compose.dev.yml")
    .Build()
    .Start();
```

```yaml
# docker-compose.dev.yml
version: '3.8'
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
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .Build()
    .Start();

// Find by name
var webContainer = services.Containers
    .FirstOrDefault(c => c.Name.Contains("web"));

var apiContainer = services.Containers
    .FirstOrDefault(c => c.Name.Contains("api"));

// Get endpoints
var webEndpoint = webContainer?.ToHostExposedEndpoint("80/tcp");
var apiEndpoint = apiContainer?.ToHostExposedEndpoint("8080/tcp");
```

### Execute Commands

```csharp
var dbContainer = services.Containers
    .FirstOrDefault(c => c.Name.Contains("db"));

// Run database migration
var result = await dbContainer.ExecAsync(
    "psql", "-U", "postgres", "-c",
    "CREATE TABLE IF NOT EXISTS users (id SERIAL PRIMARY KEY);"
);
```

## WordPress with MySQL Example

### docker-compose.yml

```yaml
version: '3.8'
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

  wordpress:
    image: wordpress:latest
    depends_on:
      - db
    ports:
      - "80"
    environment:
      WORDPRESS_DB_HOST: db:3306
      WORDPRESS_DB_USER: wordpress
      WORDPRESS_DB_PASSWORD: wordpress

volumes:
  db_data:
```

### C# Code

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .WaitForHttp("wordpress",
        url: null,  // Auto-detect from port mapping
        continuation: (response, _) =>
            response.Body.Contains("WordPress") ? 0 : 500)
    .Build()
    .Start();

var wpContainer = services.Containers
    .First(c => c.Name.Contains("wordpress"));

var endpoint = wpContainer.ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"WordPress: http://localhost:{endpoint.Port}");
```

## Kafka with Zookeeper Example

### docker-compose.yml

```yaml
version: '3.8'
services:
  zookeeper:
    image: confluentinc/cp-zookeeper:7.4.0
    environment:
      ZOOKEEPER_CLIENT_PORT: 2181
      ZOOKEEPER_TICK_TIME: 2000

  kafka:
    image: confluentinc/cp-kafka:7.4.0
    depends_on:
      - zookeeper
    ports:
      - "9092"
    environment:
      KAFKA_BROKER_ID: 1
      KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:29092,PLAINTEXT_HOST://localhost:9092
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT
      KAFKA_INTER_BROKER_LISTENER_NAME: PLAINTEXT
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
```

### C# Code

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .WaitForPort("zookeeper", "2181/tcp", 30000)
    .WaitForPort("kafka", "9092/tcp", 30000)
    .Build()
    .Start();

var kafkaContainer = services.Containers
    .First(c => c.Name.Contains("kafka"));

var endpoint = kafkaContainer.ToHostExposedEndpoint("9092/tcp");
var bootstrapServers = $"localhost:{endpoint.Port}";

// Use with your Kafka client
Console.WriteLine($"Kafka: {bootstrapServers}");
```

## RabbitMQ Example

### docker-compose.yml

```yaml
version: '3.8'
services:
  rabbitmq:
    image: rabbitmq:3-management-alpine
    ports:
      - "5672"   # AMQP
      - "15672"  # Management UI
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
```

### C# Code

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .WaitForPort("rabbitmq", "5672/tcp", 30000)
    .WaitForHttp("rabbitmq", "http://localhost:15672/api/health/checks/alarms",
        continuation: (response, _) =>
            response.Code == HttpStatusCode.OK ? 0 : 500)
    .Build()
    .Start();

var rmqContainer = services.Containers
    .First(c => c.Name.Contains("rabbitmq"));

var amqpEndpoint = rmqContainer.ToHostExposedEndpoint("5672/tcp");
var mgmtEndpoint = rmqContainer.ToHostExposedEndpoint("15672/tcp");

Console.WriteLine($"AMQP: amqp://guest:guest@localhost:{amqpEndpoint.Port}");
Console.WriteLine($"Management: http://localhost:{mgmtEndpoint.Port}");
```

## Build Services

### Build Images

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .WithBuild()  // Build images before starting
    .Build()
    .Start();
```

### Force Rebuild

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .WithBuild()
    .WithNoBuildCache()  // Don't use cache
    .Build()
    .Start();
```

## Environment Variables

### From Environment

```csharp
// Set environment variables that compose file uses
Environment.SetEnvironmentVariable("DB_PASSWORD", "secret");

using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .Build()
    .Start();
```

### With .env File

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
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .Scale("worker", 3)  // Run 3 worker instances
    .Build()
    .Start();

var workers = services.Containers
    .Where(c => c.Name.Contains("worker"))
    .ToList();

Console.WriteLine($"Workers: {workers.Count}");  // 3
```

## Cleanup Options

### Remove Volumes on Down

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .RemoveVolumesOnDown()  // Remove volumes when stopped
    .Build()
    .Start();
```

### Keep Containers

```csharp
using var services = new Builder()
    .UseContainer()
    .UseCompose()
    .FromFile("docker-compose.yml")
    .KeepContainers()  // Don't remove on dispose
    .Build()
    .Start();

// Containers remain for debugging
```

## Integration Tests Example

```csharp
public class IntegrationTestBase : IAsyncLifetime
{
    protected ICompositeService Services { get; private set; }
    protected string ApiBaseUrl { get; private set; }

    public async Task InitializeAsync()
    {
        Services = new Builder()
            .UseContainer()
            .UseCompose()
            .FromFile("docker-compose.test.yml")
            .RemoveOrphans()
            .WaitForPort("db", "5432/tcp", 30000)
            .WaitForHttp("api", "http://localhost:8080/health")
            .Build()
            .Start();

        var apiContainer = Services.Containers
            .First(c => c.Name.Contains("api"));
        var endpoint = apiContainer.ToHostExposedEndpoint("8080/tcp");
        ApiBaseUrl = $"http://localhost:{endpoint.Port}";
    }

    public async Task DisposeAsync()
    {
        Services?.Dispose();
    }
}

public class UserApiTests : IntegrationTestBase
{
    [Fact]
    public async Task CreateUser_ReturnsCreated()
    {
        var client = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };

        var response = await client.PostAsJsonAsync("/users", new { name = "Test" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
```

## Next Steps

- [Containers](containers.html) - Individual container management
- [Networking](networking.html) - Custom networks
- [Volumes](volumes.html) - Data persistence
