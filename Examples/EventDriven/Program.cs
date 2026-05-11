using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Extensions;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;

namespace EventDriven
{
  class Program
  {
    private const string DriverId = "docker";
    private const string ContainerName = "fd-event-demo";

    static async Task Main(string[] args)
    {
      using var kernel = await FluentDockerKernel.Create()
        .WithDockerCli(DriverId, d => d.AsDefault())
        .BuildAsync();

      var streamDriver = kernel.SysCtl<IStreamDriver>(DriverId);
      var context = new DriverContext(DriverId);

      using var cts = new CancellationTokenSource();
      var eventsTask = CaptureEventsAsync(streamDriver, context, ContainerName, cts.Token);

      await using (var results = await new Builder()
        .WithinDriver(DriverId, kernel)
        .UseContainer(c => c
          .UseImage("postgres:9.6-alpine")
          .WithName(ContainerName)
          .ExposePort("5432/tcp")
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .WaitForPort("5432/tcp", 30000))
        .BuildAsync())
      {
        var container = results.GetContainer(ContainerName);
        var config = await container.InspectAsync();
        var running = ServiceRunningState.Running == config.State.ToServiceState();

        Console.WriteLine(running ? "Service is running" : "Failed to start postgres instance...");
      }

      // Allow stop/destroy events to flow before shutting down the stream.
      await Task.Delay(250);
      cts.Cancel();

      var events = await eventsTask;

      Console.WriteLine("Events:");
      foreach (var evt in events)
      {
        var name = evt.ActorAttributes != null && evt.ActorAttributes.TryGetValue("name", out var containerName)
          ? containerName
          : evt.ActorId;
        Console.WriteLine($"{evt.Timestamp:u} {evt.Type}:{evt.Action} {name}");
      }
    }

    private static async Task<List<ContainerEvent>> CaptureEventsAsync(
      IStreamDriver driver,
      DriverContext context,
      string containerName,
      CancellationToken cancellationToken)
    {
      var events = new List<ContainerEvent>();
      var config = new StreamEventsConfig();

      config.Types.Add("container");

      if (!string.IsNullOrWhiteSpace(containerName))
      {
        config.Filters["container"] = containerName;
      }

      try
      {
        await foreach (var evt in driver.StreamEventsAsync(context, config, cancellationToken))
        {
          events.Add(evt);
        }
      }
      catch (OperationCanceledException)
      {
        // Expected when cancellationToken is triggered
      }

      return events;
    }
  }
}
