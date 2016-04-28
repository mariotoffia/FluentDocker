using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDockerTest.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class FluentContainerBasicTests
  {
    [TestMethod]
    public void BuildContainerRenderServiceInStoppedMode()
    {
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("postgres:latest")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build())
      {
        Assert.AreEqual(ServiceRunningState.Stopped, container.State);
      }
    }

    [TestMethod]
    public void BuildAndStartContainerWithKeepContainerWillLeaveContainerInArchve()
    {
      string id;
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("postgres:latest")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .KeepContainer()
            .Build()
            .Start())
      {
        id = container.Id;
        Assert.IsNotNull(id);
      }

      // We shall have the container as stopped by now.
      var cont =
        new Hosts()
          .Discover()
          .Select(host => host.GetContainers().FirstOrDefault(x => x.Id == id))
          .FirstOrDefault(container => null != container);

      Assert.IsNotNull(cont);
      cont.Remove(true);
    }

    [TestMethod]
    public void BuildAndStartContainerWithCustomEnvironmentWillBeReflectedInGetConfiguration()
    {
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("postgres:latest")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var config = container.GetConfiguration();

        Assert.AreEqual(ServiceRunningState.Running, container.State);
        Assert.IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));
      }
    }

    [TestMethod]
    public void ExplicitPortMappingShouldWork()
    {
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("postgres:latest")
            .ExposePort(40001, 5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        Assert.AreEqual(40001, endpoint.Port);
      }
    }

    [TestMethod]
    public void ImplicitPortMappingShouldWork()
    {
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("postgres:latest")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        Assert.AreNotEqual(0, endpoint.Port);
      }
    }

    [TestMethod]
    public void WaitForPortShallWork()
    {
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("postgres:latest")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        Assert.AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
      }
    }

    [TestMethod]
    public void WaitForProcessShallWork()
    {
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("postgres:latest")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WaitForProcess("postgres", 30000 /*30s*/)
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        Assert.AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
      }
    }

    [TestMethod]
    public void VolumeMappingShallWork()
    {
      const string html = "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>";
      var hostPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}";
      Directory.CreateDirectory(hostPath);

      using (
        var container =
          new Builder().UseContainer()
            .UseImage("nginx:latest")
            .ExposePort(80)
            .Mount(hostPath, "/usr/share/nginx/html", MountType.ReadOnly)
            .Build()
            .Start()
            .WaitForPort("80/tcp", 30000 /*30s*/))
      {
        Assert.AreEqual(ServiceRunningState.Running, container.State);

        try
        {
          File.WriteAllText(Path.Combine(hostPath, "hello.html"), html);

          var response = $"http://{container.ToHostExposedEndpoint("80/tcp")}/hello.html".Wget();
          Assert.AreEqual(html, response);
        }
        finally
        {
          if (Directory.Exists(hostPath))
          {
            Directory.Delete(hostPath, true);
          }
        }
      }
    }

    [TestMethod]
    public void CopyFromRunningContainerShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}";
      Directory.CreateDirectory(fullPath);
      try
      {
        using (new Builder().UseContainer()
          .UseImage("postgres:latest")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .Build()
          .Start()
          .CopyFrom("/bin", fullPath))
        {
          var files = Directory.EnumerateFiles(Path.Combine(fullPath, "bin")).ToArray();
          Assert.IsTrue(files.Any(x => x.EndsWith("bash")));
          Assert.IsTrue(files.Any(x => x.EndsWith("cat")));
        }
      }
      finally
      {
        if (Directory.Exists(fullPath))
        {
          Directory.Delete(fullPath, true);
        }
      }
    }

    [TestMethod]
    public void CopyBeforeDisposeContainerShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}";
      Directory.CreateDirectory(fullPath);
      try
      {
        using (new Builder().UseContainer()
          .UseImage("postgres:latest")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .CopyOnDispose("/bin", fullPath)
          .Build()
          .Start())
        {
        }

        var files = Directory.EnumerateFiles(Path.Combine(fullPath, "bin")).ToArray();
        Assert.IsTrue(files.Any(x => x.EndsWith("bash")));
        Assert.IsTrue(files.Any(x => x.EndsWith("cat")));
      }
      finally
      {
        if (Directory.Exists(fullPath))
        {
          Directory.Delete(fullPath, true);
        }
      }
    }

    [TestMethod]
    public void ExportToTarFileWhenDisposeShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}\export.tar";
      // ReSharper disable once AssignNullToNotNullAttribute
      Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
      try
      {
        using (new Builder().UseContainer()
          .UseImage("postgres:latest")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .ExportOnDispose(fullPath)
          .Build()
          .Start())
        {
        }

        Assert.IsTrue(File.Exists(fullPath));
      }
      finally
      {
        if (File.Exists(fullPath))
        {
          // ReSharper disable once AssignNullToNotNullAttribute
          Directory.Delete(Path.GetDirectoryName(fullPath), true);
        }
      }
    }

    [TestMethod]
    public void ExportExploadedWhenDisposeShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}";
      Directory.CreateDirectory(fullPath);
      try
      {
        using (new Builder().UseContainer()
          .UseImage("postgres:latest")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .ExportExploadedOnDispose(fullPath)
          .Build()
          .Start())
        {
        }

        Assert.IsTrue(Directory.Exists(fullPath));

        var files = Directory.GetFiles(fullPath).ToArray();
        Assert.IsTrue(files.Any(x => x.Contains("docker-entrypoint.sh")));
      }
      finally
      {
        if (Directory.Exists(fullPath))
        {
          Directory.Delete(fullPath, true);
        }
      }
    }

    [TestMethod]
    public void ExportWithConditionDisposeShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}\export.tar";
      // ReSharper disable once AssignNullToNotNullAttribute
      Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

      // Probably the oppsite is reverse where the last statement in the using clause
      // would set failure = false - but this is a unit test ;)
      var failure = false;
      try
      {
        // ReSharper disable once AccessToModifiedClosure
        using (new Builder().UseContainer()
          .UseImage("postgres:latest")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .ExportOnDispose(fullPath, svc => failure)
          .Build()
          .Start())
        {
          failure = true;
        }

        Assert.IsTrue(File.Exists(fullPath));
      }
      finally
      {
        if (File.Exists(fullPath))
        {
          // ReSharper disable once AssignNullToNotNullAttribute
          Directory.Delete(Path.GetDirectoryName(fullPath), true);
        }
      }
    }

    [TestMethod]
    public void CopyToRunningContainerShallWork()
    {
      var fullPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}\hello.html";

      // ReSharper disable once AssignNullToNotNullAttribute
      Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
      File.WriteAllText(fullPath, "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>");

      try
      {
        IList<Diff> before;
        using (
          var container =
            new Builder().UseContainer()
              .UseImage("postgres:latest")
              .ExposePort(5432)
              .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
              .Build()
              .Start()
              .WaitForProcess("postgres", 30000 /*30s*/)
              .Diff(out before)
              .CopyTo("/bin", fullPath))
        {
          var after = container.Diff();

          Assert.IsFalse(before.Any(x => x.Item == "/bin/hello.html"));
          Assert.IsTrue(after.Any(x => x.Item == "/bin/hello.html"));
        }
      }
      finally
      {
        if (Directory.Exists(fullPath))
        {
          Directory.Delete(fullPath, true);
        }
      }
    }
  }
}