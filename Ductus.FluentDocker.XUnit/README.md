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

This library enables `docker` and `docker-compose` interactions using a _Fluent API_. It is supported on Linux, Windows and Mac. It also has support for the legacy `docker-machine` interactions.

**Have a look at the [project site](https://github.com/mariotoffia/FluentDocker) for more information.**

**Sample Fluent API usage**
```cs
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("kiasaki/alpine-postgres")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        Assert.AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
      }
```
The following snippet fires up Postgres and waits for it to be ready. It uses docker-compose file to perform the task.
```cs
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      using (var svc = new Builder()
                        .UseContainer()
                        .UseCompose()
                        .FromFile(file)
                        .RemoveOrphans()
                        .WaitForHttp("wordpress", "http://localhost:8000/wp-admin/install.php") 
                        .Build().Start())
      {
        // We now have a running WordPress with a MySql database        
        var installPage = await "http://localhost:8000/wp-admin/install.php".Wget();

        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
        Assert.AreEqual(1, svc.Hosts.Count); // The host used by compose
        Assert.AreEqual(2, svc.Containers.Count); // We can access each individual container
        Assert.AreEqual(2, svc.Images.Count); // And the images used.
      }
```

👀 It has tons of features, including a low-level command style, services and finally, the Fluent API on top of it. 