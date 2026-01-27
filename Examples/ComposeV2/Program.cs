using FluentDocker.Builders;
using FluentDocker.Extensions;
using FluentDocker.Kernel;
using FluentDocker.Model.Common;
using FluentDocker.Services;

namespace ComposeV2;

/// <summary>
/// Demonstrates FluentDocker v3 features:
/// - Docker Compose V2 (uses 'docker compose' command)
/// - Directory copy to/from containers
/// - TemplateString path interpolation
/// </summary>
class Program
{
  private const string DriverId = "docker";

  static async Task Main(string[] args)
  {
    Console.WriteLine("FluentDocker v3 - Compose V2 & Directory Copy Example");
    Console.WriteLine("======================================================\n");

    using var kernel = await FluentDockerKernel.Create()
      .WithDriver(DriverId, d => d.UseDockerCli().AsDefault())
      .BuildAsync();

    await RunComposeExampleAsync(kernel);
    await RunDirectoryCopyExampleAsync(kernel);
    await RunTemplateStringExampleAsync();
  }

  /// <summary>
  /// Docker Compose V2 example using 'docker compose' (not docker-compose binary).
  /// </summary>
  private static async Task RunComposeExampleAsync(FluentDockerKernel kernel)
  {
    Console.WriteLine("1. Docker Compose V2 Example");
    Console.WriteLine("----------------------------");

    var composeFile = Path.Combine(AppContext.BaseDirectory, "docker-compose.yml");
    if (!File.Exists(composeFile))
    {
      Console.WriteLine($"  Skipping: docker-compose.yml not found at {composeFile}");
      Console.WriteLine("  To run this example, create a docker-compose.yml file.\n");
      return;
    }

    await using var results = await new Builder()
      .WithinDriver(DriverId, kernel)
      .UseCompose(c => c
        .WithComposeFile(composeFile)
        .WithRemoveOrphans())
      .BuildAsync();

    Console.WriteLine($"Services started: {results.Containers.Count}");
    foreach (var container in results.Containers)
    {
      var config = await container.InspectAsync();
      Console.WriteLine($"  - {container.Name}: {config.State.ToServiceState()}");
    }
    Console.WriteLine();
  }

  /// <summary>
  /// Directory copy operations (copy directories to/from containers).
  /// </summary>
  private static async Task RunDirectoryCopyExampleAsync(FluentDockerKernel kernel)
  {
    Console.WriteLine("2. Directory Copy Example");
    Console.WriteLine("-------------------------");

    // Create a temp directory with some files using TemplateString
    var tempPath = new TemplateString("${TEMP}/fd-copy-demo-${RND}").ToString();
    Directory.CreateDirectory(tempPath);

    var dataDir = Path.Combine(tempPath, "data");
    Directory.CreateDirectory(dataDir);
    await File.WriteAllTextAsync(Path.Combine(dataDir, "config.json"), """{"name": "FluentDocker", "version": "3.0"}""");
    await File.WriteAllTextAsync(Path.Combine(dataDir, "settings.txt"), "key=value\nlog_level=debug");

    Console.WriteLine($"Created temp directory: {tempPath}");
    Console.WriteLine($"  - data/config.json");
    Console.WriteLine($"  - data/settings.txt");

    await using var results = await new Builder()
      .WithinDriver(DriverId, kernel)
      .UseContainer(c => c
        .UseImage("alpine:latest")
        .WithCommand("tail", "-f", "/dev/null"))  // Keep container running
      .BuildAsync();

    var container = results.Containers.First();
    Console.WriteLine($"\nContainer: {container.Name}");

    // Copy directory TO container
    Console.WriteLine("\nCopying directory TO container...");
    await container.CopyToAsync(dataDir, "/app/data");
    Console.WriteLine("  Copied: data/ -> /app/data/");

    // Verify by listing files
    var output = await container.ExecuteAsync("ls -la /app/data");
    Console.WriteLine("\n  Files in container /app/data:");
    foreach (var line in output.Split('\n').Take(5))
    {
      Console.WriteLine($"    {line}");
    }

    // Copy directory FROM container
    var downloadPath = Path.Combine(tempPath, "downloaded");
    Console.WriteLine($"\nCopying directory FROM container...");
    await container.CopyFromToPathAsync("/app/data", downloadPath);
    Console.WriteLine($"  Copied: /app/data/ -> {downloadPath}/");

    // List downloaded files
    if (Directory.Exists(downloadPath))
    {
      Console.WriteLine("\n  Downloaded files:");
      foreach (var file in Directory.GetFiles(downloadPath, "*", SearchOption.AllDirectories))
      {
        Console.WriteLine($"    {Path.GetRelativePath(downloadPath, file)}");
      }
    }

    // Cleanup temp directory
    try { Directory.Delete(tempPath, recursive: true); } catch { /* ignore */ }
    Console.WriteLine();
  }

  /// <summary>
  /// TemplateString path interpolation examples.
  /// </summary>
  private static async Task RunTemplateStringExampleAsync()
  {
    Console.WriteLine("3. TemplateString Examples");
    Console.WriteLine("--------------------------");

    // Basic templates
    var tempDir = new TemplateString("${TEMP}");
    Console.WriteLine($"  ${{TEMP}}     -> {tempDir}");

    var randomPath = new TemplateString("${TEMP}/test-${RND}");
    Console.WriteLine($"  ${{TEMP}}/test-${{RND}} -> {randomPath}");

    var pwdPath = new TemplateString("${PWD}/config");
    Console.WriteLine($"  ${{PWD}}/config -> {pwdPath}");

    // Environment variables with E_ prefix
    var homePath = new TemplateString("${E_HOME}");
    Console.WriteLine($"  ${{E_HOME}}   -> {homePath}");

    var userPath = new TemplateString("${E_USER}");
    Console.WriteLine($"  ${{E_USER}}   -> {userPath}");

    // Default values
    var customPath = new TemplateString("${E_CUSTOM_VAR:-/default/path}");
    Console.WriteLine($"  ${{E_CUSTOM_VAR:-/default/path}} -> {customPath}");

    // Nested templates
    var nestedPath = new TemplateString("${TEMP}/${E_USER:-anonymous}/session-${RND}");
    Console.WriteLine($"  Nested template -> {nestedPath}");

    Console.WriteLine("\nExample complete.");
    await Task.CompletedTask;
  }
}
