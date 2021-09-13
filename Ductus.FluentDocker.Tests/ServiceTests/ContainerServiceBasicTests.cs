using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Ductus.FluentDocker.Tests.ServiceTests
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
      _host = hosts.FirstOrDefault(x => x.IsNative) ?? hosts.FirstOrDefault(x => x.Name == "default");

      if (null != _host && _host.State != ServiceRunningState.Running)
      {
        _host.Start();
        _host.Host.LinuxMode(_host.Certificates);
        return;
      }

      if (null == _host && hosts.Count > 0)
        _host = hosts.First();

      if (null == _host)
      {
        if (_createdHost)
          throw new Exception("Failed to initialize the test class, tried to create a docker host but failed");

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
        "test-machine".Delete(true /*force*/);
    }

    [TestMethod]
    public void CreateContainerMakesServiceStopped()
    {
      using (var container = _host.Create("postgres:9.6-alpine", false,
        new ContainerCreateParams
        {
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        Assert.AreEqual(ServiceRunningState.Stopped, container.State);
      }
    }

    [TestMethod]
    public void Issue_69_Container_name_lost_first_char()
    {
      // ReSharper disable once InconsistentNaming
      using (var container_x = _host.Create("nginx:1.13.6-alpine", prms: new ContainerCreateParams
      {
        Privileged = true,
        Name = "container_X",
        Interactive = true,
        Tty = true
      }))
      {
        Assert.AreEqual("container_X", container_x.Name);
      }
    }

    [TestMethod]
    public void CreateAndStartContainerWithEnvironment()
    {
      using (var container = _host.Create("postgres:9.6-alpine", false,
        new ContainerCreateParams
        {
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        container.Start();
        var config = container.GetConfiguration();

        Assert.AreEqual(ServiceRunningState.Running, container.State);
        Assert.IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));
      }
    }

    [TestMethod]
    public void DeleteVolumesOnContainerDisposeShallWork()
    {
      using (var container = _host.Create("postgres:9.6-alpine", false,
        new ContainerCreateParams
        {
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }, deleteVolumeOnDispose: true))
      {
        container.Start();
        var config = container.GetConfiguration();

        Assert.AreEqual(ServiceRunningState.Running, container.State);
        Assert.IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));
      }
    }

    [TestMethod]
    public void DeleteNamedVolumesOnContainerDisposeShallWork()
    {
      try
      {
        _host.Host.VolumeCreate("unit-test-volume");

        using (var container = _host.Create("postgres:9.6-alpine", false,
          new ContainerCreateParams
          {
            Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" },
            Volumes = new[] { "unit-test-volume:/world" }
          }, deleteNamedVolumeOnDispose: true))
        {
          container.Start();
          var config = container.GetConfiguration();

          Assert.AreEqual(ServiceRunningState.Running, container.State);
          Assert.IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));
        }
      }
      finally
      {
        _host.Host.VolumeRm(volume: "unit-test-volume");
      }
    }

    [TestMethod]
    public void ListVolumesShallWork()
    {
      try
      {
        _host.Host.VolumeCreate("unit-test-volume");
        var volumes = _host.Host.VolumeInspect(volume: "unit-test-volume");
        Assert.IsTrue(volumes.Success);
        Assert.AreEqual("unit-test-volume", volumes.Data[0].Name);
      }
      finally
      {
        _host.Host.VolumeRm(volume: "unit-test-volume");
      }
    }

    [TestMethod]
    public void CreateAndStartContainerWithOneExposedPortVerified()
    {
      using (var container = _host.Create("postgres:9.6-alpine", false,
        new ContainerCreateParams
        {
          PublishAllPorts = true,
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        container.Start().WaitForRunning();
        var config = container.GetConfiguration();

        Assert.AreEqual(ServiceRunningState.Running, container.State);
        Assert.IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));

        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        endpoint.WaitForPort(10000 /*10s*/);
        Debug.Write($"Succeeded waiting for postgres port {endpoint} docker port 5432/tcp");
      }
    }

    [TestMethod]
    public void ProcessesInContainerAndManuallyVerifyPostgresIsRunning()
    {
      using (var container = _host.Create("postgres:9.6-alpine", false,
        new ContainerCreateParams
        {
          PublishAllPorts = true,
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        container.Start();
        Assert.AreEqual(ServiceRunningState.Running, container.State);

        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        endpoint.WaitForPort(10000 /*10s*/);

        var proc = container.GetRunningProcesses();
        Debug.WriteLine(proc.ToString());

        Assert.IsTrue(proc.Rows.Any(x => x.Command == "bash /usr/local/bin/docker-entrypoint.sh postgres"));
      }
    }

    [TestMethod]
    public void ExportRunningContainerToTarFileShallSucceed()
    {
      using (var container = _host.Create("postgres:9.6-alpine", false,
        new ContainerCreateParams
        {
          PublishAllPorts = true,
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        container.Start();
        Assert.AreEqual(ServiceRunningState.Running, container.State);

        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        endpoint.WaitForPort(10000 /*10s*/);

        var rnd = Path.GetFileName(Path.GetTempFileName());
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), rnd);

        try
        {
          var path = container.Export(fullPath);
          Assert.IsNotNull(path);
          Assert.IsTrue(File.Exists(fullPath));
        }
        finally
        {
          if (File.Exists(fullPath))
            File.Delete(fullPath);
        }
      }
    }

    [TestMethod]
    public void ExportRunningContainerExploadedShallSucceed()
    {
      using (var container = _host.Create("postgres:9.6-alpine", false,
        new ContainerCreateParams
        {
          PublishAllPorts = true,
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        container.Start();
        Assert.AreEqual(ServiceRunningState.Running, container.State);

        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        endpoint.WaitForPort(10000 /*10s*/);

        var rnd = Path.GetFileName(Path.GetTempFileName());
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), rnd);

        try
        {
          container.Export(fullPath, true);
          Assert.IsTrue(Directory.Exists(fullPath));

          var files = Directory.GetFiles(fullPath).ToArray();
          Assert.IsTrue(files.Any(x => x.Contains("docker-entrypoint.sh")));
        }
        finally
        {
          if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);
        }
      }
    }

    [TestMethod]
    public async Task UseHostVolumeInsideContainerWhenMountedShallSucceed()
    {
      const string html = "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>";
      var fullPath = (TemplateString)@"${TEMP}\fluentdockertest\${RND}";
      Directory.CreateDirectory(fullPath);

      using (var container = _host.Create("nginx:1.13.6-alpine", false,
        new ContainerCreateParams
        {
          PortMappings = new[] { "80" },
          Volumes = new[] { $"{fullPath.Rendered}:/usr/share/nginx/html:ro" }
        }))
      {
        container.Start();
        Assert.AreEqual(ServiceRunningState.Running, container.State);

        var endpoint = container.ToHostExposedEndpoint("80/tcp");
        endpoint.WaitForPort(10000 /*10s*/);

        File.WriteAllText(Path.Combine(fullPath, "hello.html"), html);

        var response = await $"http://{endpoint}/hello.html".Wget();
        Assert.AreEqual(html, response);
      }
    }

    [TestMethod]
    public void CopyFromRunningContainerShallWork()
    {
      using (var container = _host.Create("postgres:9.6-alpine", false,
        new ContainerCreateParams
        {
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        container.Start();
        Assert.AreEqual(ServiceRunningState.Running, container.State);

        var fullPath = (TemplateString)@"${TEMP}\fluentdockertest\${RND}";
        try
        {
          Directory.CreateDirectory(fullPath);
          container.CopyFrom("/etc", fullPath);

          var files = Directory.EnumerateFiles(Path.Combine(fullPath, "etc")).ToArray();
          Assert.IsTrue(files.Any(x => x.EndsWith("fstab")));
        }
        finally
        {
          if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);
        }
      }
    }

    [TestMethod]
    public void CopyToRunningContainerShallWork()
    {
      using (var container = _host.Create("postgres:9.6-alpine", false,
        new ContainerCreateParams
        {
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        container.Start();
        Assert.AreEqual(ServiceRunningState.Running, container.State);

        var fullPath = (TemplateString)@"${TEMP}/fluentdockertest/${RND}/hello.html";

        try
        {
          // ReSharper disable once AssignNullToNotNullAttribute
          Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
          File.WriteAllText(fullPath, "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>");

          var before = container.Diff();
          container.CopyTo("/bin", fullPath);
          var after = container.Diff();

          Assert.IsFalse(before.Any(x => x.Item == "/bin/hello.html"));
          Assert.IsTrue(after.Any(x => x.Item == "/bin/hello.html"));
        }
        finally
        {
          if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);
        }
      }
    }

    [TestMethod]
    public void WaitingForSpecificStatesShallWork()
    {
      using (var container = _host.Create("postgres:9.6-alpine"))
      {
        Assert.AreEqual(ServiceRunningState.Stopped, container.State);

        container.Start();
        container.WaitForRunning();
        Assert.AreEqual(ServiceRunningState.Running, container.State);

        container.Stop();
        container.WaitForStopped();
        Assert.AreEqual(ServiceRunningState.Stopped, container.State);
      }
    }

    [TestMethod]
    public void GetContainersShallWork()
    {
      // Arrange
      using (var container = _host.Create("postgres:9.6-alpine"))
      using (var container2 = _host.Create("postgres:9.6-alpine"))
      {
        container.Start();

        // Act
        var result = _host.GetContainers(true);

        // Assert
        Assert.IsNotNull(result.SingleOrDefault(c => c.Id == container.Id
                                                     && JsonConvert.SerializeObject(c) == JsonConvert.SerializeObject(container)));
        Assert.IsNotNull(result.SingleOrDefault(c => c.Id == container2.Id
                                                     && JsonConvert.SerializeObject(c) == JsonConvert.SerializeObject(container2)));
      }
    }
  }
}
