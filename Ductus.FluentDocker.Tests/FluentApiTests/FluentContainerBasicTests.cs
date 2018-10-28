using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
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
    }

    [TestMethod]
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
    public async Task VolumeMappingShallWork()
    {
      const string html = "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>";
      var hostPath = (TemplateString) @"${TEMP}/fluentdockertest/${RND}";
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
          if (Directory.Exists(hostPath)) Directory.Delete(hostPath, true);
        }
      }
    }

    [TestMethod]
    public async Task VolumeMappingWithSpacesShallWork()
    {
      const string html = "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>";
      var hostPath = (TemplateString) @"${TEMP}/fluentdockertest/with space in path/${RND}";
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
          if (Directory.Exists(hostPath)) Directory.Delete(hostPath, true);
        }
      }
    }

    [TestMethod]
    public void CopyFromRunningContainerShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}/fluentdockertest/${RND}";
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
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
      }
    }

    [TestMethod]
    public void CopyBeforeDisposeContainerShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}/fluentdockertest/${RND}";
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
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
      }
    }

    [TestMethod]
    public void ExportToTarFileWhenDisposeShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}/fluentdockertest/${RND}/export.tar";
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
        if (File.Exists(fullPath)) Directory.Delete(Path.GetDirectoryName(fullPath), true);
      }
    }

    [TestMethod]
    public void ExportExploadedWhenDisposeShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}/fluentdockertest/${RND}";
      Directory.CreateDirectory(fullPath);
      try
      {
        using (Fd.UseContainer()
          .UseImage("postgres:9.6-alpine")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .ExportExploadedOnDispose(fullPath)
          .Build()
          .Start())
        {
        }

        IsTrue(Directory.Exists(fullPath));

        var files = Directory.GetFiles(fullPath).ToArray();
        IsTrue(files.Any(x => x.Contains("docker-entrypoint.sh")));
      }
      finally
      {
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
      }
    }

    [TestMethod]
    public void ExportWithConditionDisposeShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}/fluentdockertest/${RND}/export.tar";
      // ReSharper disable once AssignNullToNotNullAttribute
      Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

      // Probably the oppsite is reverse where the last statement in the using clause
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
        if (File.Exists(fullPath)) Directory.Delete(Path.GetDirectoryName(fullPath), true);
      }
    }

    [TestMethod]
    public void CopyToRunningContainerShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}/fluentdockertest/${RND}/hello.html";

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
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
      }
    }

    [TestMethod]
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
  }
}