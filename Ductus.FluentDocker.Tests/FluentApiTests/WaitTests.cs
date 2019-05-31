using System;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using static Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Ductus.FluentDocker.Tests.FluentApiTests
{
  [TestClass]
  public class WaitTests
  {
    private static bool _checkConnectionInvoked;

    [TestMethod]
    public void SingleWaitLambdaShallGetInvoked()
    {
      _checkConnectionInvoked = false;
      using (var container = Fd.UseContainer()
        .UseImage("postgres:9.6-alpine")
        .ExposePort(5432)
        .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
        .Wait("", (service, count) => CheckConnection(count, service))
        .Build()
        .Start())
      {
        IsTrue(_checkConnectionInvoked, "Invoked since container was created and thus run phase is executed");
      }
    }

    [TestMethod]
    public void WaitLambdaWithReusedContainerShallGetInvoked()
    {
      _checkConnectionInvoked = false;
      using (var c1 = Fd.UseContainer()
        .UseImage("postgres:9.6-alpine")
        .WithName("postgres")
        .ExposePort(5432)
        .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
        .ReuseIfExists()
        .WaitForPort("5432/tcp", TimeSpan.FromSeconds(30))
        .Build()
        .Start())
      {
        // Make sure to have named container running
        var config = c1.GetConfiguration();
        AreEqual(ServiceRunningState.Running, c1.State);

        using (var c2 = Fd.UseContainer()
          .UseImage("postgres:9.6-alpine")
          .WithName("postgres")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .ReuseIfExists()
          .Wait("", (service, count) => CheckConnection(count, service))
          .Build()
          .Start())
        {
          IsTrue(_checkConnectionInvoked,
            "Is invoked even if reused container since Start drives the container state eventually to running");
        }
      }
    }

    private static int CheckConnection(int count, IContainerService service)
    {
      _checkConnectionInvoked = true;

      if (count > 10) throw new FluentDockerException("Failed to wait for sql server");

      var ep = service.ToHostExposedEndpoint("5432/tcp");
      var str = $"Server={ep.Address};Port={ep.Port};Userid=postgres;" +
                "Password=mysecretpassword;Pooling=false;MinPoolSize=1;" +
                "MaxPoolSize=20;Timeout=15;SslMode=Disable;Database=postgres";

      try
      {
        using (var conn = new NpgsqlConnection(str))
        {
          conn.Open();
          return 0;
        }
      }
      catch
      {
        return 500 /*ms*/;
      }
    }
  }
}