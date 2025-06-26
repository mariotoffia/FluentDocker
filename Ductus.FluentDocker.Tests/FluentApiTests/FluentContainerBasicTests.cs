using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services; 
using Ductus.FluentDocker.Services.Extensions;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Ductus.FluentDocker.Tests.FluentApiTests
{
  [TestClass]
  public class FluentContainerBasicTests
  {
    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      Utilities.LinuxMode();
      Utilities.EnsureImage("postgres:9.6-alpine", TimeSpan.FromMinutes(1.0));
    }

    [TestMethod]
    [TestCategory("CI")]
    public void VersionInfoShallBePossibleToRetrieve()
    {
      var v = Fd.Version();
      Assert.IsTrue(v != null && v.Length > 0);
    }

    [TestMethod]
    [TestCategory("CI")]
    public void BuildContainerRenderServiceInStoppedMode()
    {
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:9.6-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build())
      {
        AreEqual(ServiceRunningState.Stopped, container.State);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void UseStaticBuilderWillAlwaysRunDisposeOnContainer()
    {
      Fd.Container(c => c.UseContainer()
          .UseImage("postgres:9.6-alpine")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .WaitForPort("5432/tcp", TimeSpan.FromSeconds(30)),
        svc =>
        {
          var config = svc.GetConfiguration();
          AreEqual(ServiceRunningState.Running, svc.State);
          IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));
        });
    }

    [TestMethod]
    [TestCategory("CI")]
    public void UseStaticBuilderAsExtension()
    {
      var build = Fd.UseContainer()
        .UseImage("postgres:9.6-alpine")
        .ExposePort(5432)
        .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
        .WaitForPort("5432/tcp", TimeSpan.FromSeconds(30));

      build.Container(svc =>
      {
        var config = svc.GetConfiguration();
        AreEqual(ServiceRunningState.Running, svc.State);
        IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));
      });
    }

    [TestMethod]
    [TestCategory("CI")]
    public void BuildAndStartContainerWithKeepContainerWillLeaveContainerInArchive()
    {
      string id;
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:9.6-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .KeepContainer()
            .Build()
            .Start())
      {
        id = container.Id;
        IsNotNull(id);
      }

      // We shall have the container as stopped by now.
      var cont =
        Fd.Discover()
          .Select(host => host.GetContainers().FirstOrDefault(x => x.Id == id))
          .FirstOrDefault(container => null != container);

      IsNotNull(cont);
      cont.Remove(true);
    }

    [TestMethod]
    [TestCategory("CI")]
    public void BuildAndStartContainerWithCustomEnvironmentWillBeReflectedInGetConfiguration()
    {
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:9.6-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var config = container.GetConfiguration();

        AreEqual(ServiceRunningState.Running, container.State);
        IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void PauseAndResumeShallWorkOnSingleContainer()
    {
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:9.6-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        AreEqual(ServiceRunningState.Running, container.State);

        container.Pause();
        AreEqual(ServiceRunningState.Paused, container.State);
        var config = container.GetConfiguration(true);
        AreEqual(ServiceRunningState.Paused, config.State.ToServiceState());

        container.Start();
        AreEqual(ServiceRunningState.Running, container.State);
        config = container.GetConfiguration(true);
        AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void ExplicitPortMappingShouldWork()
    {
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:9.6-alpine")
            .ExposePort(40001, 5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        AreEqual(40001, endpoint.Port);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void ImplicitPortMappingShouldWork()
    {
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:9.6-alpine")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        AreNotEqual(0, endpoint.Port);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void FullImplicitPortMappingShouldWork()
    {
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:9.6-alpine")
            .ExposeAllPorts()
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        AreNotEqual(0, endpoint.Port);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void ExposeAllPortsIsMutuallyExclusiveWithExposePort()
    {
      var exception = ThrowsException<FluentDockerNotSupportedException>(() => Fd.UseContainer().ExposePort(5432).ExposeAllPorts());
      AreEqual("ExposeAllPorts is mutually exclusive with ExposePort methods. Do not call ExposePort if you want to expose all ports.", exception.Message);
    }

    [TestMethod]
    [TestCategory("CI")]
    public void ExposePortIsMutuallyExclusiveWithExposeAllPorts()
    {
      var exception = ThrowsException<FluentDockerNotSupportedException>(() => Fd.UseContainer().ExposeAllPorts().ExposePort(5432));
      AreEqual("ExposePort is mutually exclusive with ExposeAllPorts methods. Do not call ExposeAllPorts if you want to explicitly expose ports.", exception.Message);
    }

    [TestMethod]
    [TestCategory("CI")]
    public void WaitForPortShallWork()
    {
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:9.6-alpine")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void WaitForProcessShallWork()
    {
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:9.6-alpine")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WaitForProcess("postgres", 30000 /*30s*/)
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
      }
    }
    [TestMethod]
    [TestCategory("Volume")]
    public async Task VolumeMappingShallWork()
    {
      const string html = "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>";
      var hostPath = (TemplateString)@"${TEMP}/fluentdockertest/${RND}";
      Directory.CreateDirectory(hostPath);

      using (
        var container =
          Fd.UseContainer()
            .UseImage("nginx:1.13.6-alpine")
            .ExposePort(80)
            .Mount(hostPath, "/usr/share/nginx/html", MountType.ReadOnly)
            .Build()
            .Start()
            .WaitForPort("80/tcp", 30000 /*30s*/))
      {
        AreEqual(ServiceRunningState.Running, container.State);

        try
        {
          File.WriteAllText(Path.Combine(hostPath, "hello.html"), html);

          var response = await $"http://{container.ToHostExposedEndpoint("80/tcp")}/hello.html".Wget();
          AreEqual(html, response);
        }
        finally
        {
          if (Directory.Exists(hostPath))
            Directory.Delete(hostPath, true);
        }
      }
    }

    [TestMethod]
    [TestCategory("Volume")]
    public async Task VolumeMappingWithSpacesShallWork()
    {
      const string html = "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>";
      var hostPath = (TemplateString)@"${TEMP}/fluentdockertest/with space in path/${RND}";
      Directory.CreateDirectory(hostPath);

      using (
        var container =
          Fd.UseContainer()
            .UseImage("nginx:1.13.6-alpine")
            .ExposePort(80)
            .Mount(hostPath, "/usr/share/nginx/html", MountType.ReadOnly)
            .WaitForPort("80/tcp", 30000 /*30s*/)
            .Build()
            .Start())
      {
        AreEqual(ServiceRunningState.Running, container.State);

        try
        {
          File.WriteAllText(Path.Combine(hostPath, "hello.html"), html);

          var response = await $"http://{container.ToHostExposedEndpoint("80/tcp")}/hello.html".Wget();
          AreEqual(html, response);
        }
        finally
        {
          if (Directory.Exists(hostPath))
            Directory.Delete(hostPath, true);
        }
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void CopyFromRunningContainerShallWork()
    {
      var fullPath = (TemplateString)@"${TEMP}/fluentdockertest/${RND}";
      Directory.CreateDirectory(fullPath);
      try
      {
        using (Fd.UseContainer()
          .UseImage("postgres:9.6-alpine")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .Build()
          .Start()
          .CopyFrom("/", fullPath))
        {
          var files = Directory.EnumerateFiles(fullPath).ToArray();
          IsTrue(files.Any(x => x.EndsWith(".dockerenv")));
        }
      }
      finally
      {
        if (Directory.Exists(fullPath))
          Directory.Delete(fullPath, true);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void CopyBeforeDisposeContainerShallWork()
    {
      var fullPath = (TemplateString)@"${TEMP}/fluentdockertest/${RND}";
      Directory.CreateDirectory(fullPath);
      try
      {
        using (Fd.UseContainer()
          .UseImage("postgres:9.6-alpine")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .CopyOnDispose("/", fullPath)
          .Build()
          .Start())
        {
        }

        var files = Directory.EnumerateFiles(fullPath).ToArray();
        IsTrue(files.Any(x => x.EndsWith(".dockerenv")));
      }
      finally
      {
        if (Directory.Exists(fullPath))
          Directory.Delete(fullPath, true);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void ExportToTarFileWhenDisposeShallWork()
    {
      var fullPath = (TemplateString)@"${TEMP}/fluentdockertest/${RND}/export.tar";
      // ReSharper disable once AssignNullToNotNullAttribute
      Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
      try
      {
        using (Fd.UseContainer()
          .UseImage("postgres:9.6-alpine")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .ExportOnDispose(fullPath)
          .Build()
          .Start())
        {
        }

        IsTrue(File.Exists(fullPath));
      }
      finally
      {
        if (File.Exists(fullPath))
          Directory.Delete(Path.GetDirectoryName(fullPath), true);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void ExportExplodedWhenDisposeShallWork()
    {
      var fullPath = (TemplateString)@"${TEMP}/fluentdockertest/${RND}";
      Directory.CreateDirectory(fullPath);
      try
      {
        using (Fd.UseContainer()
          .UseImage("postgres:9.6-alpine")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .ExportExplodedOnDispose(fullPath)
          .Build()
          .Start())
        {
        }

        IsTrue(Directory.Exists(fullPath));

        var files = Directory.GetFiles(fullPath).ToArray();
        IsTrue(files.Any(x => x.Contains(".dockerenv")));
      }
      finally
      {
        if (Directory.Exists(fullPath))
          Directory.Delete(fullPath, true);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void ExportWithConditionDisposeShallWork()
    {
      var fullPath = (TemplateString)@"${TEMP}/fluentdockertest/${RND}/export.tar";
      // ReSharper disable once AssignNullToNotNullAttribute
      Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

      // Probably the opposite is reverse where the last statement in the using clause
      // would set failure = false - but this is a unit test ;)
      var failure = false;
      try
      {
        // ReSharper disable once AccessToModifiedClosure
        using (Fd.UseContainer()
          .UseImage("postgres:9.6-alpine")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .ExportOnDispose(fullPath, svc => failure)
          .Build()
          .Start())
        {
          failure = true;
        }

        IsTrue(File.Exists(fullPath));
      }
      finally
      {
        if (File.Exists(fullPath))
          Directory.Delete(Path.GetDirectoryName(fullPath), true);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void CopyToRunningContainerShallWork()
    {
      var fullPath = (TemplateString)@"${TEMP}/fluentdockertest/${RND}/hello.html";

      // ReSharper disable once AssignNullToNotNullAttribute
      Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
      File.WriteAllText(fullPath, "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>");

      try
      {
        using (
          var container =
            Fd.UseContainer()
              .UseImage("postgres:9.6-alpine")
              .ExposePort(5432)
              .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
              .Build()
              .Start()
              .WaitForProcess("postgres", 30000 /*30s*/)
              .Diff(out var before)
              .CopyTo("/bin", fullPath))
        {
          var after = container.Diff();

          IsFalse(before.Any(x => x.Item == "/bin/hello.html"));
          IsTrue(after.Any(x => x.Item == "/bin/hello.html"));
        }
      }
      finally
      {
        if (Directory.Exists(fullPath))
          Directory.Delete(fullPath, true);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void ReuseOfExistingContainerShallWork()
    {
      using (Fd
        .UseContainer()
        .UseImage("postgres:9.6-alpine")
        .WithName("reusable-name")
        .Build())
      using (Fd
        .UseContainer()
        .ReuseIfExists()
        .UseImage("postgres:9.6-alpine")
        .WithName("reusable-name")
        .Build())
      {
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void PullContainerBeforeRunningShallWork()
    {
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:latest", true)
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WaitForProcess("postgres", 30000 /*30s*/)
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void ContainerHealthCheckShallWork()
    {
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:latest", true)
            .HealthCheck("exit")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        AreEqual(HealthState.Starting, config.State.Health.Status);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void ContainerWithUlimitsShallWork()
    {
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:latest", true)
            .UseUlimit(Ulimit.NoFile, 2048, 2048)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
      }
    }

    [TestMethod]
    [TestCategory("CI")]
    public void ContainerWithMemoryLimitShallWork()
    {
      using (
        var container =
        Fd.UseContainer()
          .UseImage("postgres:latest", true)
          .WithMemoryLimit("2g")
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .Build()
          .Start())
      {
        var config = container.GetConfiguration(true);
      }
    }
  }
}
