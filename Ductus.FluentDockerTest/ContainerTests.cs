using Ductus.FluentDocker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class ContainerTests
  {
    [TestMethod]
    public void EmptyBuildAndToDockerShallWorkWhenEnvIsCorrectlySet()
    {
      var builder = new DockerBuilder();
      using (var container = builder.Build())
      {
        container.Dispose();
      }
    }

 
    [TestMethod]
    public void CreatePostgresImageAndStart()
    {
      using (
        var container =
          new DockerBuilder()
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WithImage("postgres:latest")
            .Build())
      {
        container.Create().Start();
      }
    }

    [TestMethod]
    public void CreatePostgresImageAndVerifyOpenPort()
    {
      // to try out manually - docker exec -it pgsql sh -c 'exec psql -U postgres'
      using (
        var container =
          new DockerBuilder()
            .WithImage("postgres:latest")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .ExposePorts("5432")
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .Build())
      {
        container.Create().Start();
      }
    }
  }
}