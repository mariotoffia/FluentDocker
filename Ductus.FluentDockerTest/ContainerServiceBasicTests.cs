using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Ductus.FluentDocker;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model;
using Ductus.FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class ContainerServiceBasicTests
  {
    private static IHostService _host;
    private static bool _createdHost;

    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      var hosts = new Hosts().Discover();
      _host = hosts.FirstOrDefault(x => x.Name == "default");
      if (null != _host && _host.State != ServiceRunningState.Running)
      {
        _host.Start();
      }

      if (null == _host && hosts.Count > 0)
      {
        _host = hosts.First();
      }

      if (null == _host)
      {
        if (_createdHost)
        {
          throw new Exception("Failed to initialize the test class, tried to create a docker host but failed");
        }

        var res = "test-machine".Create(1024, 20000, 1);
        Assert.AreEqual(true, res.Success);

        var start = "test-machine".Start();
        Assert.AreEqual(true, start.Success);

        _createdHost = true;
        Initialize(ctx);
      }
    }

    [ClassCleanup]
    public static void TearDown()
    {
      if (_createdHost)
      {
        "test-machine".Delete(true /*force*/);
      }
    }

    [TestMethod]
    public void CreateContainerMakesServiceStopped()
    {
      using (var container = _host.Create("postgres:latest",
        new ContainerCreateParams
        {
          Environment = new[] {"POSTGRES_PASSWORD=mysecretpassword"}
        }))
      {
        Assert.AreEqual(ServiceRunningState.Stopped, container.State);
      }
    }

    [TestMethod]
    public void CreateAndStartContainerWithEnvironment()
    {
      using (var container = _host.Create("postgres:latest",
        new ContainerCreateParams
        {
          Environment = new[] {"POSTGRES_PASSWORD=mysecretpassword"}
        }))
      {
        container.Start();
        var config = container.GetConfiguration();

        Assert.AreEqual(ServiceRunningState.Running, container.State);
        Assert.IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));
      }
    }

    [TestMethod]
    public void CreateAndStartContainerWithOneExposedPortVerified()
    {
      using (var container = _host.Create("postgres:latest",
        new ContainerCreateParams
        {
          PortMappings = new[] {"40001:5432"},
          Environment = new[] {"POSTGRES_PASSWORD=mysecretpassword"}
        }))
      {
        container.Start();
        var config = container.GetConfiguration();

        Assert.AreEqual(ServiceRunningState.Running, container.State);
        Assert.IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));

        var endpoint = container.ToHosExposedtPort("5432/tcp");
        endpoint.WaitForPort(10000 /*10s*/);
        Debug.Write($"Succeeded waiting for postgres port {endpoint} docker port 5432/tcp");
      }
    }
    [TestMethod]
    public void ProcessesInContainerAndManuallyVerifyPostgresIsRunning()
    {
      using (var container = _host.Create("postgres:latest",
        new ContainerCreateParams
        {
          PortMappings = new[] { "40001:5432" },
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        container.Start();
        Assert.AreEqual(ServiceRunningState.Running, container.State);

        var endpoint = container.ToHosExposedtPort("5432/tcp");
        endpoint.WaitForPort(10000 /*10s*/);

        var proc = container.GetRunningProcesses();
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
    public void ExportRunningContainerToTarFileShallSucceed()
    {
      using (var container = _host.Create("postgres:latest",
        new ContainerCreateParams
        {
          PortMappings = new[] { "40001:5432" },
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        container.Start();
        Assert.AreEqual(ServiceRunningState.Running, container.State);

        var endpoint = container.ToHosExposedtPort("5432/tcp");
        endpoint.WaitForPort(10000 /*10s*/);

        var rnd = Path.GetFileName(Path.GetTempFileName());
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), rnd);

        try
        {
          var path = container.Export((TemplateString) fullPath);
          Assert.IsNotNull(path);
          Assert.IsTrue(File.Exists(fullPath));
        }
        finally
        {
          if (File.Exists(fullPath))
          {
            File.Delete(fullPath);
          }
        }
      }
    }

    [TestMethod]
    public void ExportRunningContainerExploadedShallSucceed()
    {
      using (var container = _host.Create("postgres:latest",
        new ContainerCreateParams
        {
          PortMappings = new[] { "40001:5432" },
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        container.Start();
        Assert.AreEqual(ServiceRunningState.Running, container.State);

        var endpoint = container.ToHosExposedtPort("5432/tcp");
        endpoint.WaitForPort(10000 /*10s*/);

        var rnd = Path.GetFileName(Path.GetTempFileName());
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), rnd);

        string path = null;
        try
        {
          path = container.Export((TemplateString)fullPath, true);
          Assert.IsNotNull(path);
          Assert.IsTrue(Directory.Exists(path));

          var files = Directory.GetFiles(path).ToArray();
          Assert.IsTrue(files.Any(x => x.Contains("docker-entrypoint.sh")));
        }
        finally
        {
          if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
          {
            Directory.Delete(path, true);
          }
        }
      }
    }
  }
}