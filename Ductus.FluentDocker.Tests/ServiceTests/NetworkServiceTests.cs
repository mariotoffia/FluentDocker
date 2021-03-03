using System;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ServiceTests
{
  [TestClass]
  public sealed class NetworkServiceTests
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
    public void DiscoverNetworksShallWork()
    {
      var networks = _host.GetNetworks();
      Assert.IsTrue(networks.Count > 0);
      Assert.IsTrue(networks.Count(x => x.Name == "bridge") == 1);
      Assert.IsTrue(networks.Count(x => x.Name == "host") == 1);
    }

    [TestMethod]
    public void NetworkIsDeletedWhenDisposedAndFlagIsSet()
    {
      using (var nw = _host.CreateNetwork("unit-test-network", removeOnDispose: true))
      {
        Assert.IsTrue(nw.Id.Length > 0);
        Assert.AreEqual("unit-test-network", nw.Name);
      }

      var networks = _host.GetNetworks();
      Assert.IsTrue(networks.Count(x => x.Name == "unit-test-network") == 0);
    }

    [TestMethod]
    public void AttachkWithAliasShallWorkWithIContainerService()
    {
      using (var nw = _host.CreateNetwork("unit-test-network", removeOnDispose: true))
      {
        var alias = "hello";
        var testerImageName = "hello-world-tester";

        var helloContainerBuilder = new Builders.Builder().UseContainer()
          .UseImage("crccheck/hello-world");

        var testerImageBuilder = new Builders.Builder().DefineImage(testerImageName)
          .From("alpine")
          .Run(
            "apk add curl"
          )
          .Command(
            "curl", $"http://{alias}:8000"
          );

        var testerContainerBuilder = new Builders.Builder().UseContainer()
          .UseNetwork(nw)
          .UseImage(testerImageName);


        using (var helloContainer = helloContainerBuilder.Build())
        using (var testerImage = testerImageBuilder.Build())
        using (var testerContainer = testerContainerBuilder.Build())
        {
          helloContainer.Start();
          nw.Attach(helloContainer, true, alias);

          testerContainer.Start();
          var logs = string.Join(
            "\n",
            testerContainer.DockerHost.Logs(testerContainer.Id).ReadToEnd());

          Assert.IsTrue(logs.IndexOf("Hello World") >= 0);
        }
      }
    }

    [TestMethod]
    public void AttachkWithAliasShallWorkWithContainerId()
    {
      using (var nw = _host.CreateNetwork("unit-test-network", removeOnDispose: true))
      {
        var alias = "hello";
        var testerImageName = "hello-world-tester";

        var helloContainerBuilder = new Builders.Builder().UseContainer()
          .UseImage("crccheck/hello-world");

        var testerImageBuilder = new Builders.Builder().DefineImage(testerImageName)
          .From("alpine")
          .Run(
            "apk add curl"
          )
          .Command(
            "curl", $"http://{alias}:8000"
          );

        var testerContainerBuilder = new Builders.Builder().UseContainer()
          .UseNetwork(nw)
          .UseImage(testerImageName);


        using (var helloContainer = helloContainerBuilder.Build())
        using (var testerImage = testerImageBuilder.Build())
        using (var testerContainer = testerContainerBuilder.Build())
        {
          helloContainer.Start();
          nw.Attach(helloContainer.Id, true, alias);

          testerContainer.Start();
          var logs = string.Join(
            "\n",
            testerContainer.DockerHost.Logs(testerContainer.Id).ReadToEnd());

          Assert.IsTrue(logs.IndexOf("Hello World") >= 0);
        }
      }
    }

    [TestMethod]
    public void UseNetworkWithAliasShallWorkWithINetworkService()
    {
      using (var nw = _host.CreateNetwork("unit-test-network", removeOnDispose: true))
      {
        var alias = "hello";
        var testerImageName = "hello-world-tester";

        var helloContainerBuilder = new Builders.Builder().UseContainer()
          .UseImage("crccheck/hello-world")
          .UseNetworksWithAlias(alias, nw);

        var testerImageBuilder = new Builders.Builder().DefineImage(testerImageName)
          .From("alpine")
          .Run(
            "apk add curl"
          )
          .Command(
            "curl", $"http://{alias}:8000"
          );

        var testerContainerBuilder = new Builders.Builder().UseContainer()
          .UseNetwork(nw)
          .UseImage(testerImageName);


        using (var helloContainer = helloContainerBuilder.Build())
        using (var testerImage = testerImageBuilder.Build())
        using (var testerContainer = testerContainerBuilder.Build())
        {
          helloContainer.Start();
          testerContainer.Start();
          var logs = string.Join(
            "\n",
            testerContainer.DockerHost.Logs(testerContainer.Id).ReadToEnd());

          Assert.IsTrue(logs.IndexOf("Hello World") >= 0);
        }
      }
    }

    [TestMethod]
    public void UseNetworkWithAliasShallWorkWithString()
    {
      using (var nw = _host.CreateNetwork("unit-test-network", removeOnDispose: true))
      {
        var alias = "hello";
        var testerImageName = "hello-world-tester";

        var helloContainerBuilder = new Builders.Builder().UseContainer()
          .UseImage("crccheck/hello-world")
          .UseNetworksWithAlias(alias, nw.Name);

        var testerImageBuilder = new Builders.Builder().DefineImage(testerImageName)
          .From("alpine")
          .Run(
            "apk add curl"
          )
          .Command(
            "curl", $"http://{alias}:8000"
          );

        var testerContainerBuilder = new Builders.Builder().UseContainer()
          .UseNetwork(nw)
          .UseImage(testerImageName);


        using (var helloContainer = helloContainerBuilder.Build())
        using (var testerImage = testerImageBuilder.Build())
        using (var testerContainer = testerContainerBuilder.Build())
        {
          helloContainer.Start();
          testerContainer.Start();
          var logs = string.Join(
            "\n",
            testerContainer.DockerHost.Logs(testerContainer.Id).ReadToEnd());

          Assert.IsTrue(logs.IndexOf("Hello World") >= 0);
        }
      }
    }

    [TestMethod]
    public void MultipleUseNetworkWithAliasShallWorkWithINetworkService()
    {
      using (var nw1 = _host.CreateNetwork("unit-test-network-1", removeOnDispose: true))
      using (var nw2 = _host.CreateNetwork("unit-test-network-2", removeOnDispose: true))
      {
        var alias1 = "first-hello";
        var alias2 = "second-hello";
        var testerImageAlias1Name = $"hello-world-tester-{alias1}";
        var testerImageAlias2Name = $"hello-world-tester-{alias2}";

        var helloContainerBuilder = new Builders.Builder().UseContainer()
          .UseImage("crccheck/hello-world")
          .UseNetworksWithAlias(alias1, nw1)
          .UseNetworksWithAlias(alias2, nw2);

        var testerImageAlias1Builder = new Builders.Builder().DefineImage(testerImageAlias1Name)
          .From("alpine")
          .Run(
            "apk add curl"
          )
          .Command(
            "curl", $"http://{alias1}:8000"
          );

        var testerImageAlias2Builder = new Builders.Builder().DefineImage(testerImageAlias2Name)
          .From("alpine")
          .Run(
            "apk add curl"
          )
          .Command(
            "curl", $"http://{alias2}:8000"
          );

        var testerContainerAlias1Builder = new Builders.Builder().UseContainer()
          .UseNetwork(nw1)
          .UseImage(testerImageAlias1Name);

        var testerContainerAlias2Builder = new Builders.Builder().UseContainer()
          .UseNetwork(nw2)
          .UseImage(testerImageAlias2Name);


        using (var helloContainer = helloContainerBuilder.Build())
        using (var testerImageAlias1Image = testerImageAlias1Builder.Build())
        using (var testerImageAlias2Image = testerImageAlias2Builder.Build())
        using (var testerContainerAlias1 = testerContainerAlias1Builder.Build())
        using (var testerContainerAlias2 = testerContainerAlias2Builder.Build())
        {
          helloContainer.Start();

          testerContainerAlias1.Start();
          var logsAlias1 = string.Join(
            "\n",
            testerContainerAlias1.DockerHost.Logs(testerContainerAlias1.Id).ReadToEnd());

          Assert.IsTrue(logsAlias1.IndexOf("Hello World") >= 0);

          testerContainerAlias2.Start();
          var logsAlias2 = string.Join(
            "\n",
            testerContainerAlias2.DockerHost.Logs(testerContainerAlias2.Id).ReadToEnd());

          Assert.IsTrue(logsAlias2.IndexOf("Hello World") >= 0);
        }
      }
    }

    [TestMethod]
    public void MultipleUseNetworkWithAliasShallWorkWithString()
    {
      using (var nw1 = _host.CreateNetwork("unit-test-network-1", removeOnDispose: true))
      using (var nw2 = _host.CreateNetwork("unit-test-network-2", removeOnDispose: true))
      {
        var alias1 = "first-hello";
        var alias2 = "second-hello";
        var testerImageAlias1Name = $"hello-world-tester-{alias1}";
        var testerImageAlias2Name = $"hello-world-tester-{alias2}";

        var helloContainerBuilder = new Builders.Builder().UseContainer()
          .UseImage("crccheck/hello-world")
          .UseNetworksWithAlias(alias1, nw1.Name)
          .UseNetworksWithAlias(alias2, nw2.Name);

        var testerImageAlias1Builder = new Builders.Builder().DefineImage(testerImageAlias1Name)
          .From("alpine")
          .Run(
            "apk add curl"
          )
          .Command(
            "curl", $"http://{alias1}:8000"
          );

        var testerImageAlias2Builder = new Builders.Builder().DefineImage(testerImageAlias2Name)
          .From("alpine")
          .Run(
            "apk add curl"
          )
          .Command(
            "curl", $"http://{alias2}:8000"
          );

        var testerContainerAlias1Builder = new Builders.Builder().UseContainer()
          .UseNetwork(nw1)
          .UseImage(testerImageAlias1Name);

        var testerContainerAlias2Builder = new Builders.Builder().UseContainer()
          .UseNetwork(nw2)
          .UseImage(testerImageAlias2Name);


        using (var helloContainer = helloContainerBuilder.Build())
        using (var testerImageAlias1Image = testerImageAlias1Builder.Build())
        using (var testerImageAlias2Image = testerImageAlias2Builder.Build())
        using (var testerContainerAlias1 = testerContainerAlias1Builder.Build())
        using (var testerContainerAlias2 = testerContainerAlias2Builder.Build())
        {
          helloContainer.Start();

          testerContainerAlias1.Start();
          var logsAlias1 = string.Join(
            "\n",
            testerContainerAlias1.DockerHost.Logs(testerContainerAlias1.Id).ReadToEnd());

          Assert.IsTrue(logsAlias1.IndexOf("Hello World") >= 0);

          testerContainerAlias2.Start();
          var logsAlias2 = string.Join(
            "\n",
            testerContainerAlias2.DockerHost.Logs(testerContainerAlias2.Id).ReadToEnd());

          Assert.IsTrue(logsAlias2.IndexOf("Hello World") >= 0);
        }
      }
    }

    [TestMethod]
    public void MultipleUseNetworkWithSameAliasShallWorkWithINetworkService()
    {
      using (var nw1 = _host.CreateNetwork("unit-test-network-1", removeOnDispose: true))
      using (var nw2 = _host.CreateNetwork("unit-test-network-2", removeOnDispose: true))
      {
        var alias = "hello";
        var testerImageName = $"hello-world-tester";

        var helloContainerBuilder = new Builders.Builder().UseContainer()
          .UseImage("crccheck/hello-world")
          .UseNetworksWithAlias(alias, nw1, nw2);

        var testerImageBuilder = new Builders.Builder().DefineImage(testerImageName)
          .From("alpine")
          .Run(
            "apk add curl"
          )
          .Command(
            "curl", $"http://{alias}:8000"
          );

        var testerContainerBuilder1 = new Builders.Builder().UseContainer()
          .UseNetwork(nw1)
          .UseImage(testerImageName);

        var testerContainerBuilder2 = new Builders.Builder().UseContainer()
          .UseNetwork(nw2)
          .UseImage(testerImageName);


        using (var helloContainer = helloContainerBuilder.Build())
        using (var testerImage = testerImageBuilder.Build())
        using (var testerContainer1 = testerContainerBuilder1.Build())
        using (var testerContainer2 = testerContainerBuilder2.Build())
        {
          helloContainer.Start();

          testerContainer1.Start();
          var logsAlias1 = string.Join(
            "\n",
            testerContainer1.DockerHost.Logs(testerContainer1.Id).ReadToEnd());

          Assert.IsTrue(logsAlias1.IndexOf("Hello World") >= 0);

          testerContainer2.Start();
          var logsAlias2 = string.Join(
            "\n",
            testerContainer2.DockerHost.Logs(testerContainer2.Id).ReadToEnd());

          Assert.IsTrue(logsAlias2.IndexOf("Hello World") >= 0);
        }
      }
    }

    [TestMethod]
    public void MultipleUseNetworkWithSameAliasShallWorkWithString()
    {
      using (var nw1 = _host.CreateNetwork("unit-test-network-1", removeOnDispose: true))
      using (var nw2 = _host.CreateNetwork("unit-test-network-2", removeOnDispose: true))
      {
        var alias = "hello";
        var testerImageName = $"hello-world-tester";

        var helloContainerBuilder = new Builders.Builder().UseContainer()
          .UseImage("crccheck/hello-world")
          .UseNetworksWithAlias(alias, nw1.Name, nw2.Name);

        var testerImageBuilder = new Builders.Builder().DefineImage(testerImageName)
          .From("alpine")
          .Run(
            "apk add curl"
          )
          .Command(
            "curl", $"http://{alias}:8000"
          );

        var testerContainerBuilder1 = new Builders.Builder().UseContainer()
          .UseNetwork(nw1)
          .UseImage(testerImageName);

        var testerContainerBuilder2 = new Builders.Builder().UseContainer()
          .UseNetwork(nw2)
          .UseImage(testerImageName);


        using (var helloContainer = helloContainerBuilder.Build())
        using (var testerImage = testerImageBuilder.Build())
        using (var testerContainer1 = testerContainerBuilder1.Build())
        using (var testerContainer2 = testerContainerBuilder2.Build())
        {
          helloContainer.Start();

          testerContainer1.Start();
          var logsAlias1 = string.Join(
            "\n",
            testerContainer1.DockerHost.Logs(testerContainer1.Id).ReadToEnd());

          Assert.IsTrue(logsAlias1.IndexOf("Hello World") >= 0);

          testerContainer2.Start();
          var logsAlias2 = string.Join(
            "\n",
            testerContainer2.DockerHost.Logs(testerContainer2.Id).ReadToEnd());

          Assert.IsTrue(logsAlias2.IndexOf("Hello World") >= 0);
        }
      }
    }
  }
}
