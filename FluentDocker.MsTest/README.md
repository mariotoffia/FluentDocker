# FluentDocker for MsTest

> **OBSOLETE**: This package is deprecated. Use `FluentDocker.Testing.MsTest` with
> `FluentDocker.Testing.Core` instead. See
> [migration guide](docs/testing/migration-from-legacy.md) for side-by-side examples.

In addition to the standard _FluentDocker_ usage, it adds the ability to use easy testing with containers via MsTest.

For example, fire up a Postgres container inside the test:

```cs
[TestClass]
public class PostgresMsTests : PostgresTestBase
{
    [TestMethod]
    public void CanConnect()
    {
        // ConnectionString is available from base class
        using var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();
        Assert.AreEqual(ConnectionState.Open, conn.State);
    }
}
```

**Have a look at the [project site](https://github.com/mariotoffia/FluentDocker) for more information.**

**Sample Fluent API usage**
```cs
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();

using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .ExposePort("5432")
        .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
        .WaitForPort("5432/tcp", 30000))
    .Build();

var container = results.Containers.First();
var config = container.GetConfiguration(true);
Assert.AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
```
