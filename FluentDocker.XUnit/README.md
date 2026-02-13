# FluentDocker for XUnit

In addition to the standard _FluentDocker_ usage, it adds the ability to use easy testing with containers via _XUnit_.

For example, fire up a Postgres container inside the test could look like this

```cs
public class PostgresXUnitTests : IClassFixture<PostgresTestBase>
{
    [Fact]
    public void Test()
    {
          // We now have a running Postgres
          // and a valid connection string to use.
    }
}
```

This library enables `docker` and `docker compose` interactions using a _Fluent API_. It is supported on Linux, Windows and Mac.

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
