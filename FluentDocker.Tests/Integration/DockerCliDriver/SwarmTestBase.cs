using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Exception used to skip tests when prerequisites are not available.
  /// Uses xUnit dynamic skip convention ($XunitDynamicSkip$).
  /// </summary>
  public class DockerSkipException : Exception
  {
    public DockerSkipException(string message)
        : base("$XunitDynamicSkip$" + message) { }
  }

  /// <summary>
  /// Base class for tests that require Docker Swarm mode.
  /// Initializes Swarm if not already active, and leaves on teardown if we started it.
  /// Implements IAsyncLifetime directly to ensure proper xUnit lifecycle dispatch.
  /// </summary>
  [Trait("Category", "DevLocal")]
  public abstract class SwarmTestBase : IAsyncLifetime
  {
    protected FluentDockerKernel Kernel { get; private set; }
    protected string DriverId => "docker";
    protected DriverContext Context => new DriverContext(DriverId);

    protected const string TestImage = "alpine:latest";
    protected const string NginxImage = "nginx:alpine";

    /// <summary>Label applied to all test-created containers for easy cleanup.</summary>
    protected const string TestLabelKey = "com.fluentdocker.test";
    protected const string TestLabelValue = "integration";

    protected IContainerDriver ContainerDriver => Kernel.SysCtl<IContainerDriver>(DriverId);
    protected IImageDriver ImageDriver => Kernel.SysCtl<IImageDriver>(DriverId);
    protected IStackDriver StackDriver => Kernel.SysCtl<IStackDriver>(DriverId);
    protected IServiceDriver ServiceDriver => Kernel.SysCtl<IServiceDriver>(DriverId);

    public async Task InitializeAsync()
    {
      if (!IsDockerInstalled())
        throw new DockerSkipException("Docker is not installed or not in PATH");

      Kernel = await FluentDockerKernel.Create()
          .WithDockerCli(DriverId, d => d.AsDefault())
          .BuildAsync();

      if (!IsSwarmActive())
      {
        var initResult = RunDockerCommand("swarm init");
        if (initResult.exitCode != 0 && !IsSwarmActive())
          throw new DockerSkipException(
              $"Failed to initialize Docker Swarm: {initResult.error}");
      }
    }

    public Task DisposeAsync()
    {
      // Note: We intentionally do NOT leave Swarm mode here.
      // xUnit runs test classes in parallel, so leaving Swarm would break
      // other Swarm-dependent tests still running. Since these are DevLocal
      // tests, Swarm is expected to persist between test runs.
      // To leave Swarm manually: docker swarm leave --force

      Kernel?.Dispose();
      return Task.CompletedTask;
    }

    protected async Task EnsureImageAsync(string image)
    {
      var parts = image.Split(':');
      var name = parts[0];
      var tag = parts.Length > 1 ? parts[1] : "latest";
      await ImageDriver.PullAsync(Context, name, tag);
    }

    protected async Task<string> RunContainerAsync(
        string image, ContainerCreateConfig config = null)
    {
      config ??= new ContainerCreateConfig();
      config.Image = image;
      config.Detach = true;
      config.Labels ??= new Dictionary<string, string>();
      config.Labels[TestLabelKey] = TestLabelValue;
      var result = await ContainerDriver.RunAsync(Context, config);
      Assert.True(result.Success, $"Failed to run container: {result.Error}");
      return result.Data.Id;
    }

    protected async Task RemoveContainerAsync(string containerId)
    {
      if (!string.IsNullOrEmpty(containerId))
        await ContainerDriver.RemoveAsync(Context, containerId,
            force: true, removeVolumes: true);
    }

    protected string UniqueName(string prefix = "test") =>
        $"{prefix}-{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// Waits for a service to converge (running replicas match desired).
    /// </summary>
    protected async Task WaitForServiceReplicasAsync(
        string serviceName, int expectedReplicas, int maxWaitSeconds = 60)
    {
      var deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);

      while (DateTime.UtcNow < deadline)
      {
        var result = await ServiceDriver.ListAsync(Context,
            new ServiceListFilter { Name = serviceName });

        if (result.Success && result.Data?.Count > 0)
        {
          var replicas = result.Data[0].Replicas;
          if (replicas != null && replicas.Contains("/"))
          {
            var parts = replicas.Split('/');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var running)
                && int.TryParse(parts[1], out var desired)
                && running >= expectedReplicas && desired == expectedReplicas)
              return;
          }
        }

        await Task.Delay(2000);
      }
    }

    private static bool IsDockerInstalled()
    {
      var result = RunDockerCommand("--version");
      return result.exitCode == 0;
    }

    private static bool IsSwarmActive()
    {
      var result = RunDockerCommand("info --format {{.Swarm.LocalNodeState}}");
      return result.exitCode == 0
             && result.output.Trim().Equals("active", StringComparison.OrdinalIgnoreCase);
    }

    private static (int exitCode, string output, string error) RunDockerCommand(
        string arguments)
    {
      try
      {
        var process = new Process
        {
          StartInfo = new ProcessStartInfo
          {
            FileName = "docker",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
          }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(30000);

        return (process.ExitCode, output, error);
      }
      catch (Exception ex)
      {
        return (-1, string.Empty, ex.Message);
      }
    }
  }
}
