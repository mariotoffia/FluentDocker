using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker;
using FluentDocker.Builders;
using FluentDocker.Extensions;
using FluentDocker.Kernel;
using FluentDocker.Services;

namespace Simple
{
  class Program
  {
    private const string DriverId = "docker";

    static async Task Main(string[] args)
    {
      using var kernel = await CreateKernelAsync();

      await RunSingleContainerFluentApiAsync(kernel);
      // await PerformanceSingleContainerAsync(kernel);
      // await PerformanceSingleContainerFluentApiAsync(kernel);
    }

    private static Task<FluentDockerKernel> CreateKernelAsync()
    {
      return FluentDockerKernel.Create()
        .WithDriver(DriverId, d => d.UseDockerCli().AsDefault())
        .BuildAsync();
    }

    private static async Task RunSingleContainerFluentApiAsync(FluentDockerKernel kernel)
    {
      await using var results = await new Builder()
        .WithinDriver(DriverId, kernel)
        .UseContainer(c => c
          .UseImage("postgres:9.6-alpine")
          .ExposePort("5432/tcp")
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .WaitForPort("5432/tcp", 30000))
        .BuildAsync();

      var container = results.Containers.First();
      var config = await container.InspectAsync();
      var running = ServiceRunningState.Running == config.State.ToServiceState();

      Console.WriteLine(running ? "Service is running" : "Failed to start postgres instance...");
    }

    private static async Task PerformanceSingleContainerAsync(FluentDockerKernel kernel)
    {
      var stopwatch = new Stopwatch();
      stopwatch.Start();

      await using var host = Fd.GetHost(kernel, DriverId);
      Console.WriteLine("Kernel ready in {0}s", TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

      var container = await host.CreateContainerAsync(
        "nginx:alpine",
        new ContainerCreateOptions
        {
          Name = "test",
          Network = "host",
          Ports = { ["80/tcp"] = "9111", ["443/tcp"] = "9112" },
          Volumes = { "/data/log:/var/log:rw" },
          RestartPolicy = "always"
        });

      Console.WriteLine("Create container: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

      try
      {
        stopwatch.Restart();
        await container.StartAsync();

        Console.WriteLine("Start container: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);
      }
      finally
      {
        stopwatch.Restart();
        await container.DisposeAsync();
        Console.WriteLine("Dispose container: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

        stopwatch.Stop();
      }
    }

    private static async Task PerformanceSingleContainerFluentApiAsync(FluentDockerKernel kernel)
    {
      var stopwatch = new Stopwatch();
      stopwatch.Start();

      await using var results = await new Builder()
        .WithinDriver(DriverId, kernel)
        .UseContainer(c => c
          .UseImage("postgres:9.6-alpine")
          .ExposePort("5432/tcp")
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .WaitForPort("5432/tcp", 30000))
        .BuildAsync();

      Console.WriteLine("Build container: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

      var container = results.Containers.First();

      stopwatch.Restart();
      var config = await container.InspectAsync();
      Console.WriteLine("Inspect container: " + TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).TotalSeconds);

      var running = ServiceRunningState.Running == config.State.ToServiceState();
      Console.WriteLine(running ? "Service is running" : "Failed to start postgres instance...");
    }
  }
}
