using System;
using System.Linq;
using System.Diagnostics;
using Ductus.FluentDocker;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;

namespace Simple
{
  class Program
  {
    static void RunSingleContainerFluentAPI()
    {
      using (
          var container =
              new Builder().UseContainer()
                  .UseImage("postgres:9.6-alpine")
                  .ExposePort(5432)
                  .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
                  .WaitForPort("5432/tcp", 30000)
                  .Build()
                  .Start())
      {

        var config = container.GetConfiguration(true);
        var running = ServiceRunningState.Running == config.State.ToServiceState();

        Console.WriteLine(running ? "Service is running" : "Failed to start nginx instance...");

      }
    }

    static void PerformanceSingleContainer()
    {

      Stopwatch stopwatch = new Stopwatch();

      stopwatch.Start();
      var hosts = new Hosts().Discover();
      var host = hosts.FirstOrDefault(x => x.IsNative) ?? hosts.FirstOrDefault(x => x.Name == "default");

      Console.WriteLine("Hosts discovered in {0} s", TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

      var _container = host.Create("nginx:alpine",
          prms: new ContainerCreateParams
          {
            Name = "test",
            Network = "host",
            PortMappings = new string[] { "9111:80", "9112:443" },
            Volumes = new string[] { "/data/log:/var/log:rw" },
            RestartPolicy = RestartPolicy.Always
          });

      Console.WriteLine("Create container: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

      try
      {
        stopwatch.Restart();
        _container.Start();

        Console.WriteLine("Start container: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);
      }
      finally
      {

        stopwatch.Restart();
        _container.Dispose();
        Console.WriteLine("Dispose container: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

        stopwatch.Stop();

      }

    }

    static void PerformanceSingleContainerFluentAPI()
    {

      Stopwatch stopwatch = new Stopwatch();

      stopwatch.Start();
      var container = new Builder().UseContainer()
                  .UseImage("postgres:9.6-alpine")
                  .ExposePort(5432)
                  .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
                  .WaitForPort("5432/tcp", 30000)
                  .Build();

      Console.WriteLine("Build container: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

      try
      {
        stopwatch.Restart();
        container.Start();
        Console.WriteLine("Start container: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

        stopwatch.Restart();
        var config = container.GetConfiguration(true);
        Console.WriteLine("Get configuration: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

        var running = ServiceRunningState.Running == config.State.ToServiceState();
        Console.WriteLine(running ? "Service is running" : "Failed to start nginx instance...");


      }
      finally
      {

        stopwatch.Restart();
        container.Dispose();
        Console.WriteLine("Dispose container: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

        stopwatch.Stop();
      }
    }

    static void Main(string[] args)
    {
      //RunSingleContainerFluentAPI();
      //PerformanceSingleContainer();
      //PerformanceSingleContainerFluentAPI();
    }
  }
}
