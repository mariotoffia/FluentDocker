---
layout: default
title: Testing
nav_order: 8
---

# Test Support

FluentDocker provides test fixtures and base classes for MSTest and xUnit to simplify container-based testing.

## Installation

```bash
dotnet add package FluentDocker.MsTest  # For MSTest
dotnet add package FluentDocker.XUnit   # For xUnit
```

## MSTest

### FluentDockerTestBase

```csharp
using FluentDocker.MsTest;
using FluentDocker.Builders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class MyTests : FluentDockerTestBase
{
    protected override ContainerBuilder Build()
    {
        return new Builder()
            .UseContainer()
            .UseImage("nginx:alpine")
            .ExposePort(80)
            .WaitForPort("80/tcp", 30000);
    }

    [TestMethod]
    public void Container_IsRunning()
    {
        Assert.AreEqual(ServiceRunningState.Running, Container.State);
    }

    [TestMethod]
    public async Task Nginx_RespondsToRequests()
    {
        var endpoint = Container.ToHostExposedEndpoint("80/tcp");
        var response = await $"http://localhost:{endpoint.Port}".WgetAsync();
        Assert.IsTrue(response.Contains("nginx"));
    }
}
```

### Lifecycle Hooks

```csharp
[TestClass]
public class DatabaseTests : FluentDockerTestBase
{
    protected string ConnectionString { get; private set; }

    protected override ContainerBuilder Build()
    {
        return new Builder()
            .UseContainer()
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test", "POSTGRES_DB=testdb")
            .ExposePort(5432)
            .WaitForPort("5432/tcp", 30000);
    }

    protected override void OnContainerInitialized()
    {
        // Called after container starts
        var endpoint = Container.ToHostExposedEndpoint("5432/tcp");
        ConnectionString = $"Host=localhost;Port={endpoint.Port};Database=testdb;Username=postgres;Password=test";

        // Run migrations, seed data, etc.
    }

    protected override void OnContainerTearDown()
    {
        // Called before container stops
        // Export logs, cleanup, etc.
    }

    [TestMethod]
    public void Database_IsAccessible()
    {
        using var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();
        Assert.AreEqual(ConnectionState.Open, conn.State);
    }
}
```

### PostgresTestBase

```csharp
using FluentDocker.MsTest;

[TestClass]
public class PostgresTests : PostgresTestBase
{
    [TestMethod]
    public void ConnectionString_IsAvailable()
    {
        Assert.IsNotNull(ConnectionString);
        Assert.IsTrue(ConnectionString.Contains("postgres"));
    }

    [TestMethod]
    public async Task CanExecuteQueries()
    {
        using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand("SELECT 1", conn);
        var result = await cmd.ExecuteScalarAsync();

        Assert.AreEqual(1, result);
    }
}
```

## xUnit

### IClassFixture Pattern

```csharp
using FluentDocker.XUnit;
using FluentDocker.Builders;
using Xunit;

public class NginxFixture : FluentDockerTestBase
{
    protected override ContainerBuilder Build()
    {
        return new Builder()
            .UseContainer()
            .UseImage("nginx:alpine")
            .ExposePort(80)
            .WaitForPort("80/tcp", 30000);
    }
}

public class NginxTests : IClassFixture<NginxFixture>
{
    private readonly NginxFixture _fixture;

    public NginxTests(NginxFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Container_IsRunning()
    {
        Assert.Equal(ServiceRunningState.Running, _fixture.Container.State);
    }

    [Fact]
    public async Task Nginx_ReturnsWelcomePage()
    {
        var endpoint = _fixture.Container.ToHostExposedEndpoint("80/tcp");
        var response = await $"http://localhost:{endpoint.Port}".WgetAsync();
        Assert.Contains("Welcome to nginx", response);
    }
}
```

### IAsyncLifetime Pattern

```csharp
using FluentDocker.Builders;
using Xunit;

public class DatabaseTests : IAsyncLifetime
{
    private IContainerService _container;
    private string _connectionString;

    public async Task InitializeAsync()
    {
        _container = new Builder()
            .UseContainer()
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .ExposePort(5432)
            .WaitForPort("5432/tcp", 30000)
            .Build()
            .Start();

        var endpoint = _container.ToHostExposedEndpoint("5432/tcp");
        _connectionString = $"Host=localhost;Port={endpoint.Port};Database=postgres;Username=postgres;Password=test";
    }

    public Task DisposeAsync()
    {
        _container?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Database_AcceptsConnections()
    {
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        Assert.Equal(ConnectionState.Open, conn.State);
    }
}
```

### Collection Fixtures

```csharp
// Shared fixture for all tests in collection
public class SharedDatabaseFixture : IAsyncLifetime
{
    public IContainerService Container { get; private set; }
    public string ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        Container = new Builder()
            .UseContainer()
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .ExposePort(5432)
            .WaitForPort("5432/tcp", 30000)
            .Build()
            .Start();

        var endpoint = Container.ToHostExposedEndpoint("5432/tcp");
        ConnectionString = $"Host=localhost;Port={endpoint.Port};Database=postgres;Username=postgres;Password=test";
    }

    public Task DisposeAsync()
    {
        Container?.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<SharedDatabaseFixture>
{
}

[Collection("Database")]
public class UserRepositoryTests
{
    private readonly SharedDatabaseFixture _fixture;

    public UserRepositoryTests(SharedDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanCreateUser()
    {
        // Use _fixture.ConnectionString
    }
}

[Collection("Database")]
public class OrderRepositoryTests
{
    private readonly SharedDatabaseFixture _fixture;

    public OrderRepositoryTests(SharedDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanCreateOrder()
    {
        // Same database container shared
    }
}
```

## Custom Test Fixtures

### Redis Fixture

```csharp
public class RedisTestBase : FluentDockerTestBase
{
    protected string RedisConnectionString { get; private set; }
    protected IConnectionMultiplexer Redis { get; private set; }

    protected override ContainerBuilder Build()
    {
        return new Builder()
            .UseContainer()
            .UseImage("redis:alpine")
            .ExposePort(6379)
            .WaitForPort("6379/tcp", 30000);
    }

    protected override void OnContainerInitialized()
    {
        var endpoint = Container.ToHostExposedEndpoint("6379/tcp");
        RedisConnectionString = $"localhost:{endpoint.Port}";
        Redis = ConnectionMultiplexer.Connect(RedisConnectionString);
    }

    protected override void OnContainerTearDown()
    {
        Redis?.Dispose();
    }
}

[TestClass]
public class CacheTests : RedisTestBase
{
    [TestMethod]
    public async Task CanSetAndGetValue()
    {
        var db = Redis.GetDatabase();
        await db.StringSetAsync("key", "value");
        var result = await db.StringGetAsync("key");
        Assert.AreEqual("value", result.ToString());
    }
}
```

### Elasticsearch Fixture

```csharp
public class ElasticsearchTestBase : FluentDockerTestBase
{
    protected ElasticClient Client { get; private set; }

    protected override ContainerBuilder Build()
    {
        return new Builder()
            .UseContainer()
            .UseImage("elasticsearch:8.10.2")
            .WithEnvironment(
                "discovery.type=single-node",
                "xpack.security.enabled=false"
            )
            .ExposePort(9200)
            .WaitForHttp("http://localhost:9200/_cluster/health",
                continuation: (resp, _) =>
                    resp.Body.Contains("\"status\":\"green\"") ||
                    resp.Body.Contains("\"status\":\"yellow\"") ? 0 : 500,
                timeout: 60000);
    }

    protected override void OnContainerInitialized()
    {
        var endpoint = Container.ToHostExposedEndpoint("9200/tcp");
        var settings = new ConnectionSettings(new Uri($"http://localhost:{endpoint.Port}"));
        Client = new ElasticClient(settings);
    }
}
```

### Kafka Fixture

```csharp
public class KafkaTestBase : IAsyncLifetime
{
    protected ICompositeService Services { get; private set; }
    protected string BootstrapServers { get; private set; }

    public async Task InitializeAsync()
    {
        Services = new Builder()
            .UseContainer()
            .UseCompose()
            .FromFile("docker-compose.kafka.yml")
            .WaitForPort("zookeeper", "2181/tcp", 30000)
            .WaitForPort("kafka", "9092/tcp", 30000)
            .Build()
            .Start();

        var kafka = Services.Containers.First(c => c.Name.Contains("kafka"));
        var endpoint = kafka.ToHostExposedEndpoint("9092/tcp");
        BootstrapServers = $"localhost:{endpoint.Port}";

        // Wait for Kafka to be fully ready
        await Task.Delay(5000);
    }

    public Task DisposeAsync()
    {
        Services?.Dispose();
        return Task.CompletedTask;
    }
}
```

## Integration Test Patterns

### API Integration Tests

```csharp
public class ApiIntegrationTestBase : IAsyncLifetime
{
    protected ICompositeService Services { get; private set; }
    protected HttpClient Client { get; private set; }

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

        var api = Services.Containers.First(c => c.Name.Contains("api"));
        var endpoint = api.ToHostExposedEndpoint("8080/tcp");
        Client = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{endpoint.Port}")
        };
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        Services?.Dispose();
        return Task.CompletedTask;
    }
}

public class UserApiTests : ApiIntegrationTestBase
{
    [Fact]
    public async Task CreateUser_ReturnsCreated()
    {
        var response = await Client.PostAsJsonAsync("/api/users",
            new { Name = "Test User", Email = "test@example.com" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_ReturnsOk()
    {
        var response = await Client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

### Database Migration Tests

```csharp
public class MigrationTests : IAsyncLifetime
{
    private IContainerService _db;
    private string _connectionString;

    public async Task InitializeAsync()
    {
        _db = new Builder()
            .UseContainer()
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .ExposePort(5432)
            .WaitForPort("5432/tcp", 30000)
            .Build()
            .Start();

        var endpoint = _db.ToHostExposedEndpoint("5432/tcp");
        _connectionString = $"Host=localhost;Port={endpoint.Port};Database=postgres;Username=postgres;Password=test";
    }

    public Task DisposeAsync()
    {
        _db?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Migrations_ApplySuccessfully()
    {
        // Apply migrations
        var migrator = new DbMigrator(_connectionString);
        await migrator.MigrateAsync();

        // Verify schema
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'",
            conn);

        using var reader = await cmd.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("users", tables);
        Assert.Contains("orders", tables);
    }
}
```

## Test Isolation

### Per-Test Containers

```csharp
public class IsolatedTests
{
    [Fact]
    public async Task Test1_HasOwnDatabase()
    {
        using var db = new Builder()
            .UseContainer()
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .ExposePort(5432)
            .WaitForPort("5432/tcp", 30000)
            .Build()
            .Start();

        // This test has its own database
    }

    [Fact]
    public async Task Test2_HasOwnDatabase()
    {
        using var db = new Builder()
            .UseContainer()
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .ExposePort(5432)
            .WaitForPort("5432/tcp", 30000)
            .Build()
            .Start();

        // Completely isolated from Test1
    }
}
```

### Unique Names for Parallel Tests

```csharp
private static string UniqueName(string prefix) =>
    $"{prefix}-{Guid.NewGuid():N}"[..20];

// Use unique names for networks and containers in parallel tests
var networkName = UniqueName("net");
var containerName = UniqueName("db");
```

## Debugging Failed Tests

```csharp
// Keep container on failure for debugging
.KeepContainer()  // Don't remove on failure

// Export logs on failure
protected override void OnContainerTearDown()
{
    if (TestContext.CurrentTestOutcome == UnitTestOutcome.Failed)
    {
        var logs = Container.Logs();
        File.WriteAllLines($"test-logs-{DateTime.Now:yyyyMMddHHmmss}.txt", logs);
    }
}
```

## Next Steps

- [Utilities](utilities.html) - Helper utilities
- [Containers](containers.html) - Container management
- [Docker Compose](compose.html) - Multi-container tests
