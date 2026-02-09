using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Services.Impl;
using FluentDocker.Services;

namespace DockerInDockerLinux
{
  class Program
  {
    private const string DriverId = "docker";

    static async Task Main(string[] args)
    {
      using var kernel = await FluentDockerKernel.Create()
        .WithDockerCli(DriverId, d => d.AsDefault())
        .BuildAsync();

      await using var host = new HostService(kernel, DriverId, "default", isNative: true, requireTls: false);

      Console.WriteLine($"Docker driver: {DriverId}");

      var version = await host.GetVersionAsync();
      Console.WriteLine($"Server version: {version.ServerVersion}");

      var containers = await host.GetContainersAsync();
      Console.WriteLine($"Number of containers: {containers.Count}");

      foreach (var container in containers.Take(5))
      {
        Console.WriteLine($"{container.Name} ({container.Id})");
      }
    }
  }
}
