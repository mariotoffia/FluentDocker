using System.Diagnostics;
using System.IO;
using System.Linq;
using Ductus.FluentDocker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class ContainerTests
  {
    [TestMethod]
    [Ignore]
    public void EmptyBuildAndToDockerShallWorkWhenEnvIsCorrectlySet()
    {
      var builder = new DockerBuilder();
      using (var container = builder.Build())
      {
        container.Dispose();
      }
    }


    [TestMethod]
    [Ignore]
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
    [Ignore]
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
    [Ignore]
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
    [Ignore]
    public void ProcessesInContainerAndVerifyPostgresIsRunning()
    {
      using (
        var container =
          new DockerBuilder()
            .WithImage("postgres:latest")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .ExposePorts("5432")
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .WaitForProcess("postgres", 30000 /*30s*/)
            .Build())
      {
        container.Start();
        var proc = container.ContainerProcesses();
        Debug.WriteLine(proc.ToString());
      }
    }

    [TestMethod]
    [Ignore]
    public void ExportContainerWhenExceptionOccurs()
    {
      var rnd = Path.GetFileName(Path.GetTempFileName());
      var exploded = Path.Combine(Directory.GetCurrentDirectory(), rnd);

      try
      {
        using (
          var container =
            new DockerBuilder()
              .WithImage("postgres:latest")
              .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
              .ExposePorts("5432")
              .WaitForPort("5432/tcp", 30000 /*30s*/)
              .WaitForProcess("postgres", 30000 /*30s*/)
              .WhenDisposed()
              .ExportOnError("${PWD}/" + rnd)
              .Build())
        {
          container.Start();
          Assert.Fail("This will trigger an exception that will not set the Success and thus export the container");

          // ReSharper disable once HeuristicUnreachableCode
          container.Success();
        }
      }
      catch (AssertFailedException e)
      {
        try
        {
          Assert.IsTrue(
            e.Message.Contains(
              "This will trigger an exception that will not set the Success and thus export the container"));
        }
        catch (AssertFailedException)
        {
          Directory.Delete(exploded, true);
          throw;
        }
      }


      try
      {
        var files = Directory.GetFiles(exploded).ToArray();
        Assert.IsTrue(files.Any(x => x.Contains("docker-entrypoint.sh")));
      }
      finally
      {
        Directory.Delete(exploded, true);
      }
    }
  }
}