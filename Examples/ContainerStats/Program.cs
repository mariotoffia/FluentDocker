using FluentDocker.Builders;
using FluentDocker.Extensions;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;

namespace ContainerStats;

/// <summary>
/// Demonstrates FluentDocker v3 features:
/// - Container stats monitoring (CPU, memory, network)
/// - Static IPv4/IPv6 assignment
/// - Custom network creation
/// </summary>
class Program
{
  private const string DriverId = "docker";

  static async Task Main(string[] args)
  {
    Console.WriteLine("FluentDocker v3 - Container Stats & Static IP Example");
    Console.WriteLine("======================================================\n");

    using var kernel = await FluentDockerKernel.Create()
      .WithDriver(DriverId, d => d.UseDockerCli().AsDefault())
      .BuildAsync();

    await RunStatsExampleAsync(kernel);
    await RunStaticIpExampleAsync(kernel);
  }

  /// <summary>
  /// Container stats monitoring example.
  /// </summary>
  private static async Task RunStatsExampleAsync(FluentDockerKernel kernel)
  {
    Console.WriteLine("1. Container Stats Monitoring");
    Console.WriteLine("-----------------------------");

    await using var results = await new Builder()
      .WithinDriver(DriverId, kernel)
      .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80/tcp")
        .WaitForPort("80/tcp", 30000))
      .BuildAsync();

    var container = results.Containers.First();
    Console.WriteLine($"Container: {container.Name} ({container.Id[..12]})");

    // Generate some load by making HTTP requests
    var endpoint = await container.ToHostExposedEndpointAsync("80/tcp");
    var url = $"http://localhost:{endpoint.Port}";
    Console.WriteLine($"Endpoint: {url}");

    Console.WriteLine("\nGenerating load...");
    using var client = new HttpClient();
    for (int i = 0; i < 10; i++)
    {
      try { await client.GetAsync(url); } catch { /* ignore */ }
      await Task.Delay(100);
    }

    // Get container stats
    Console.WriteLine("\nContainer Stats:");
    var stats = await container.GetStatsAsync();

    Console.WriteLine($"  CPU Usage:    {stats.Cpu.UsagePercent:F2}%");
    Console.WriteLine($"  Memory:       {FormatBytes(stats.Memory.Usage)} / {FormatBytes(stats.Memory.Limit)}");
    Console.WriteLine($"  Memory %:     {stats.Memory.UsagePercent:F2}%");
    Console.WriteLine($"  Network RX:   {FormatBytes(stats.Network.RxBytes)}");
    Console.WriteLine($"  Network TX:   {FormatBytes(stats.Network.TxBytes)}");
    Console.WriteLine($"  Block Read:   {FormatBytes(stats.Disk.ReadBytes)}");
    Console.WriteLine($"  Block Write:  {FormatBytes(stats.Disk.WriteBytes)}");
    Console.WriteLine();
  }

  /// <summary>
  /// Static IP assignment example with custom network.
  /// </summary>
  private static async Task RunStaticIpExampleAsync(FluentDockerKernel kernel)
  {
    Console.WriteLine("2. Static IPv4/IPv6 Assignment");
    Console.WriteLine("------------------------------");

    // Create a custom network with specific subnet
    await using var networkResults = await new Builder()
      .WithinDriver(DriverId, kernel)
      .UseNetwork(n => n
        .WithName("fd-example-net")
        .WithSubnet("10.100.0.0/24")
        .WithGateway("10.100.0.1"))
      .BuildAsync();

    var network = networkResults.Networks.First();
    Console.WriteLine($"Network: {network.Name}");
    Console.WriteLine($"Subnet:  10.100.0.0/24");
    Console.WriteLine($"Gateway: 10.100.0.1");

    // Create container with static IP
    await using var containerResults = await new Builder()
      .WithinDriver(DriverId, kernel)
      .UseContainer(c => c
        .UseImage("nginx:alpine")
        .WithName("fd-static-ip-demo")
        .WithNetwork(network.Name)
        .UseIpV4("10.100.0.50")
        .ExposePort("80/tcp")
        .WaitForPort("80/tcp", 30000))
      .BuildAsync();

    var container = containerResults.Containers.First();
    Console.WriteLine($"\nContainer: {container.Name}");
    Console.WriteLine($"Static IP: 10.100.0.50");

    // Verify the IP assignment
    var config = await container.InspectAsync();
    var networkName = config.NetworkSettings?.Networks?.Keys.FirstOrDefault();
    if (networkName != null && config.NetworkSettings!.Networks!.TryGetValue(networkName, out var netInfo))
    {
      Console.WriteLine($"Actual IP: {netInfo.IPAddress}");
    }

    var state = config.State.ToServiceState();
    Console.WriteLine($"State:     {state}");
    Console.WriteLine();

    Console.WriteLine("Cleanup complete. Example finished.");
  }

  private static string FormatBytes(long bytes)
  {
    string[] sizes = { "B", "KB", "MB", "GB", "TB" };
    int order = 0;
    double len = bytes;
    while (len >= 1024 && order < sizes.Length - 1)
    {
      order++;
      len /= 1024;
    }
    return $"{len:0.##} {sizes[order]}";
  }
}
