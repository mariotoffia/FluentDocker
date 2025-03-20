using System;
using System.Collections.Generic;
using Ductus.FluentDocker;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Events;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;

namespace EventDriven
{
  class Program
  {
    static void Main(string[] args)
    {
      var hosts = new Hosts().Discover();
      Console.WriteLine($"Number of hosts:{hosts.Count}");

      foreach (var host in hosts)
      {
        Console.WriteLine($"{host.Host} {host.Name} {host.State}");
      }

      Console.WriteLine("Spinning up a postgres and wait for ready state...");
      using (var events = Fd.Native().Events())
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
          var image = config.Config.Image;

          Console.WriteLine(ServiceRunningState.Running == config.State.ToServiceState()
              ? "Service is running"
              : $"Failed to start {image} instance...");

          FdEvent evt;
          var list = new List<FdEvent>();
          while ((evt = events.TryRead(5000)) != null)
          {
            list.Add(evt);
          }

          Console.WriteLine("Events:");
          foreach (var e in list)
          {
            Console.WriteLine(e);
          }
        }
      }
    }
  }
}
