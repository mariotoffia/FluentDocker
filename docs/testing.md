---
layout: default
title: Testing
nav_order: 8
---

# Test Support

FluentDocker v3 provides test base classes for MSTest and xUnit that manage a
`FluentDockerKernel` and container lifecycle for you. You can also use the
standalone kernel + builder pattern when you need full control.

## Installation

```bash
dotnet add package FluentDocker.MsTest  # For MSTest
dotnet add package FluentDocker.XUnit   # For xUnit
```

## MSTest

### FluentDockerTestBase

The base class creates a kernel, builds a container via `ConfigureContainer`,
starts it, and tears it down when tests complete.

```csharp
using FluentDocker.MsTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class NginxTests : FluentDockerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("nginx:alpine")
            .ExposePort("80")
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

Override the async hooks and, optionally, the kernel factory.

```csharp
[TestClass]
public class DatabaseTests : FluentDockerTestBase
{
    protected string ConnectionString { get; private set; }

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("postgres:15-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=test")
            .WithEnvironment("POSTGRES_DB=testdb")
            .ExposePort("5432")
            .WaitForPort("5432/tcp", 30000);
    }

    protected override async Task OnContainerInitializedAsync()
    {
        var endpoint = Container.ToHostExposedEndpoint("5432/tcp");
        ConnectionString =
            $"Host=localhost;Port={endpoint.Port};Database=testdb;" +
            "Username=postgres;Password=test";
        await Task.CompletedTask;
    }

    protected override async Task OnContainerTearDownAsync()
    {
        // Export logs, cleanup, etc.
        await Task.CompletedTask;
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

### Customising the Kernel

Override `CreateKernelAsync` when you need a non-default driver or additional
kernel configuration.

```csharp
[TestClass]
public class PodmanTests : FluentDockerTestBase
{
    protected override async Task<FluentDockerKernel> CreateKernelAsync()
    {
        return await FluentDockerKernel.Create()
            .WithPodmanCli("podman", d => d.AsDefault())
            .BuildAsync();
    }

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("nginx:alpine")
            .ExposePort("80")
            .WaitForPort("80/tcp", 30000);
    }

    [TestMethod]
    public void Container_Runs_Under_Podman()
    {
        Assert.AreEqual(ServiceRunningState.Running, Container.State);
    }
}
```

## xUnit

### FluentDockerTestBase (IClassFixture)

The xUnit base class works the same way. Call `InitializeAsync()` in the
constructor or use `IAsyncLifetime` on the test class.

```csharp
using FluentDocker.XUnit;
using Xunit;

public class NginxFixture : FluentDockerTestBase
{
    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("nginx:alpine")
            .ExposePort("80")
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

### IAsyncLifetime -- Standalone Kernel + Builder

When you do not want a base class, create a kernel and use the `Builder`
directly.

```csharp
using Xunit;

public class DatabaseTests : IAsyncLifetime
{
    private FluentDockerKernel _kernel;
    private BuildResults _results;
    private IContainerService _container;
    private string _connectionString;

    public async Task InitializeAsync()
    {
        _kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        _results = await new Builder()
            .WithinDriver("docker", _kernel)
            .UseContainer(c => c
                .UseImage("postgres:15-alpine")
                .WithEnvironment("POSTGRES_PASSWORD=test")
                .ExposePort("5432")
                .WaitForPort("5432/tcp", 30000))
            .BuildAsync();

        _container = _results.Containers.First();
        var endpoint = _container.ToHostExposedEndpoint("5432/tcp");
        _connectionString =
            $"Host=localhost;Port={endpoint.Port};Database=postgres;" +
            "Username=postgres;Password=test";
    }

    public async Task DisposeAsync()
    {
        if (_results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (_kernel is IAsyncDisposable kd) await kd.DisposeAsync();
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

Share a single container across multiple test classes via xUnit collection
fixtures. Each fixture manages its own kernel.

```csharp
public class SharedDatabaseFixture : IAsyncLifetime
{
    public FluentDockerKernel Kernel { get; private set; }
    public IContainerService Container { get; private set; }
    public string ConnectionString { get; private set; }
    private BuildResults _results;

    public async Task InitializeAsync()
    {
        Kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        _results = await new Builder()
            .WithinDriver("docker", Kernel)
            .UseContainer(c => c
                .UseImage("postgres:15-alpine")
                .WithEnvironment("POSTGRES_PASSWORD=test")
                .ExposePort("5432")
                .WaitForPort("5432/tcp", 30000))
            .BuildAsync();

        Container = _results.Containers.First();
        var endpoint = Container.ToHostExposedEndpoint("5432/tcp");
        ConnectionString =
            $"Host=localhost;Port={endpoint.Port};Database=postgres;" +
            "Username=postgres;Password=test";
    }

    public async Task DisposeAsync()
    {
        if (_results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (Kernel is IAsyncDisposable kd) await kd.DisposeAsync();
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<SharedDatabaseFixture> { }

[Collection("Database")]
public class UserRepositoryTests
{
    private readonly SharedDatabaseFixture _fixture;
    public UserRepositoryTests(SharedDatabaseFixture fixture) => _fixture = fixture;

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
    public OrderRepositoryTests(SharedDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CanCreateOrder()
    {
        // Same database container shared across the collection
    }
}
```

## Custom Test Fixtures

### Redis Fixture (MSTest)

```csharp
public class RedisTestBase : FluentDockerTestBase
{
    protected string RedisConnectionString { get; private set; }
    protected IConnectionMultiplexer Redis { get; private set; }

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("redis:alpine")
            .ExposePort("6379")
            .WaitForPort("6379/tcp", 30000);
    }

    protected override async Task OnContainerInitializedAsync()
    {
        var endpoint = Container.ToHostExposedEndpoint("6379/tcp");
        RedisConnectionString = $"localhost:{endpoint.Port}";
        Redis = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
    }

    protected override async Task OnContainerTearDownAsync()
    {
        if (Redis is not null) await Redis.DisposeAsync();
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

### Elasticsearch Fixture (MSTest)

```csharp
public class ElasticsearchTestBase : FluentDockerTestBase
{
    protected ElasticClient Client { get; private set; }

    protected override void ConfigureContainer(IContainerBuilder builder)
    {
        builder
            .UseImage("elasticsearch:8.10.2")
            .WithEnvironment("discovery.type=single-node")
            .WithEnvironment("xpack.security.enabled=false")
            .ExposePort("9200")
            .WaitForHttp("http://localhost:9200/_cluster/health",
                continuation: (resp, _) =>
                    resp.Body.Contains("\"status\":\"green\"") ||
                    resp.Body.Contains("\"status\":\"yellow\"") ? 0 : 500,
                timeout: 60000);
    }

    protected override async Task OnContainerInitializedAsync()
    {
        var endpoint = Container.ToHostExposedEndpoint("9200/tcp");
        var settings = new ConnectionSettings(
            new Uri($"http://localhost:{endpoint.Port}"));
        Client = new ElasticClient(settings);
        await Task.CompletedTask;
    }
}
```

### Kafka Fixture (xUnit -- Compose)

Use the standalone kernel + compose builder for multi-container stacks.

```csharp
public class KafkaTestBase : IAsyncLifetime
{
    protected FluentDockerKernel Kernel { get; private set; }
    protected string BootstrapServers { get; private set; }
    private BuildResults _results;

    public async Task InitializeAsync()
    {
        Kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        _results = await new Builder()
            .WithinDriver("docker", Kernel)
            .UseCompose(c => c
                .WithComposeFile("docker-compose.kafka.yml")
                .WithRemoveOrphans()
                .WithWait()
                .WithWaitTimeout(30))
            .BuildAsync();

        var kafka = _results.Containers.First(c => c.Name.Contains("kafka"));
        var endpoint = kafka.ToHostExposedEndpoint("9092/tcp");
        BootstrapServers = $"localhost:{endpoint.Port}";
        await Task.Delay(5000); // Allow Kafka to stabilise
    }

    public async Task DisposeAsync()
    {
        if (_results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (Kernel is IAsyncDisposable kd) await kd.DisposeAsync();
    }
}
```

## Integration Test Patterns

### API Integration Tests (Compose)

```csharp
public class ApiIntegrationTestBase : IAsyncLifetime
{
    protected FluentDockerKernel Kernel { get; private set; }
    protected HttpClient Client { get; private set; }
    private BuildResults _results;

    public async Task InitializeAsync()
    {
        Kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        _results = await new Builder()
            .WithinDriver("docker", Kernel)
            .UseCompose(c => c
                .WithComposeFile("docker-compose.test.yml")
                .WithRemoveOrphans()
                .WithWait()
                .WithWaitTimeout(30))
            .BuildAsync();

        var api = _results.Containers.First(c => c.Name.Contains("api"));
        var endpoint = api.ToHostExposedEndpoint("8080/tcp");
        Client = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{endpoint.Port}")
        };
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (Kernel is IAsyncDisposable kd) await kd.DisposeAsync();
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
    private FluentDockerKernel _kernel;
    private BuildResults _results;
    private string _connectionString;

    public async Task InitializeAsync()
    {
        _kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        _results = await new Builder()
            .WithinDriver("docker", _kernel)
            .UseContainer(c => c
                .UseImage("postgres:15-alpine")
                .WithEnvironment("POSTGRES_PASSWORD=test")
                .ExposePort("5432")
                .WaitForPort("5432/tcp", 30000))
            .BuildAsync();

        var endpoint = _results.Containers.First()
            .ToHostExposedEndpoint("5432/tcp");
        _connectionString =
            $"Host=localhost;Port={endpoint.Port};Database=postgres;" +
            "Username=postgres;Password=test";
    }

    public async Task DisposeAsync()
    {
        if (_results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (_kernel is IAsyncDisposable kd) await kd.DisposeAsync();
    }

    [Fact]
    public async Task Migrations_ApplySuccessfully()
    {
        await new DbMigrator(_connectionString).MigrateAsync();

        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public'", conn);
        using var reader = await cmd.ExecuteReaderAsync();

        var tables = new List<string>();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));

        Assert.Contains("users", tables);
        Assert.Contains("orders", tables);
    }
}
```

## Test Isolation

### Per-Test Containers

Each test method gets its own kernel and container for full isolation.

```csharp
public class IsolatedTests
{
    [Fact]
    public async Task EachTest_HasOwnDatabase()
    {
        var kernel = await FluentDockerKernel.Create()
            .WithDockerCli("docker", d => d.AsDefault())
            .BuildAsync();

        var results = await new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c
                .UseImage("postgres:15-alpine")
                .WithEnvironment("POSTGRES_PASSWORD=test")
                .ExposePort("5432")
                .WaitForPort("5432/tcp", 30000))
            .BuildAsync();

        var container = results.Containers.First();
        Assert.NotNull(container);

        // Each test creates and disposes its own kernel + container
        if (results is IAsyncDisposable ad) await ad.DisposeAsync();
        if (kernel is IAsyncDisposable kd) await kd.DisposeAsync();
    }
}
```

### Unique Names for Parallel Tests

```csharp
private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];
```

## Debugging Failed Tests

```csharp
// Inside ConfigureContainer -- keep container on failure for debugging
builder.KeepContainer();
builder.KeepRunning();

// Export logs on failure (MSTest hook)
protected override async Task OnContainerTearDownAsync()
{
    if (TestContext.CurrentTestOutcome == UnitTestOutcome.Failed)
        await File.WriteAllLinesAsync(
            $"test-logs-{DateTime.Now:yyyyMMddHHmmss}.txt", Container.Logs());
}
```

## Next Steps
- [Utilities](utilities.html) -- [Containers](containers.html) -- [Docker Compose](compose.html)