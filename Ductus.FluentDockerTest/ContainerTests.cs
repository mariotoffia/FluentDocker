using System.Diagnostics;
using System.Linq;
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

    [TestMethod]
    public void ProcessesInContainerAndManuallyVerifyPostgresIsRunning()
    {
      using (
        var container =
          new DockerBuilder()
            .WithImage("postgres:latest")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .ExposePorts("5432")
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .Build())
      {
        container.Start();
        var proc = container.ContainerProcesses();
        Debug.WriteLine(proc.ToString());

        Assert.IsTrue(proc.Rows.Any(x => x.Command == "postgres"));
        Assert.IsTrue(proc.Rows.Any(x => x.Command == "postgres: checkpointer process"));
        Assert.IsTrue(proc.Rows.Any(x => x.Command == "postgres: writer process"));
        Assert.IsTrue(proc.Rows.Any(x => x.Command == "postgres: wal writer process"));
        Assert.IsTrue(proc.Rows.Any(x => x.Command == "postgres: autovacuum launcher process"));
        Assert.IsTrue(proc.Rows.Any(x => x.Command == "postgres: stats collector process"));
      }
    }
    [TestMethod]
    public void ProcessesInContainerAndVerifyPostgresIsRunning()
    {
      using (
        var container =
          new DockerBuilder()
            .WithImage("postgres:latest")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .ExposePorts("5432")
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .WaitForProcess("postgres",30000/*30s*/)
            .Build())
      {
        container.Start();
        var proc = container.ContainerProcesses();
        Debug.WriteLine(proc.ToString());
      }
    }
  }
}