using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging.Abstractions;
using Container = FluentDocker.Model.Containers.Container;
using DriverResponse = FluentDocker.Model.Drivers.CommandResponse<FluentDocker.Model.Drivers.Unit>;

namespace FluentDocker.Benchmarks
{
  /// <summary>
  /// Benchmarks comparing Docker REST API vs Docker CLI driver performance
  /// for common container, image, and system operations.
  /// Requires a running Docker daemon.
  /// </summary>
  [MemoryDiagnoser]
  [BenchmarkCategory("DockerApiVsCli")]
  public class DockerApiVsCliBenchmarks
  {
    private const string CliDriverId = "docker-cli";
    private const string ApiDriverId = "docker-api";
    private const string TestImage = "alpine:latest";
    private static readonly string[] SleepCommand = ["sleep", "3600"];

    private FluentDockerKernel _cliKernel = null!;
    private FluentDockerKernel _apiKernel = null!;

    private IContainerDriver _cliContainerDriver = null!;
    private IContainerDriver _apiContainerDriver = null!;
    private IImageDriver _cliImageDriver = null!;
    private IImageDriver _apiImageDriver = null!;
    private ISystemDriver _cliSystemDriver = null!;
    private ISystemDriver _apiSystemDriver = null!;

    private DriverContext _cliContext = null!;
    private DriverContext _apiContext = null!;

    private string _containerId = null!;

    [GlobalSetup]
    public async Task Setup()
    {
      // Build CLI kernel
      _cliKernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerCli(CliDriverId, d => d.AsDefault())
          .BuildAsync();

      // Build API kernel
      _apiKernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerApi(ApiDriverId, d => d.AsDefault())
          .BuildAsync();

      _cliContext = new DriverContext(CliDriverId);
      _apiContext = new DriverContext(ApiDriverId);

      // Resolve drivers
      _cliContainerDriver = _cliKernel.SysCtl<IContainerDriver>(CliDriverId);
      _apiContainerDriver = _apiKernel.SysCtl<IContainerDriver>(ApiDriverId);
      _cliImageDriver = _cliKernel.SysCtl<IImageDriver>(CliDriverId);
      _apiImageDriver = _apiKernel.SysCtl<IImageDriver>(ApiDriverId);
      _cliSystemDriver = _cliKernel.SysCtl<ISystemDriver>(CliDriverId);
      _apiSystemDriver = _apiKernel.SysCtl<ISystemDriver>(ApiDriverId);

      // Ensure image is available and create a test container
      await _cliImageDriver.PullAsync(_cliContext, "alpine", "latest");

      var runResult = await _cliContainerDriver.RunAsync(_cliContext, new ContainerCreateConfig
      {
        Image = TestImage,
        Command = SleepCommand,
        Detach = true
      });

      _containerId = runResult.Data.Id;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
      if (!string.IsNullOrEmpty(_containerId))
      {
        await _cliContainerDriver.RemoveAsync(
            _cliContext, _containerId, force: true, removeVolumes: true);
      }

      _cliKernel?.Dispose();
      _apiKernel?.Dispose();
    }

    // --- ContainerInspect ---

    [BenchmarkCategory("ContainerInspect"), Benchmark(Baseline = true)]
    public async Task<Model.Drivers.CommandResponse<Container>> InspectContainer_Cli()
    {
      return await _cliContainerDriver.InspectAsync(_cliContext, _containerId);
    }

    [BenchmarkCategory("ContainerInspect"), Benchmark]
    public async Task<Model.Drivers.CommandResponse<Container>> InspectContainer_Api()
    {
      return await _apiContainerDriver.InspectAsync(_apiContext, _containerId);
    }

    // --- ContainerList ---

    [BenchmarkCategory("ContainerList"), Benchmark(Baseline = true)]
    public async Task<Model.Drivers.CommandResponse<IList<Container>>> ListContainers_Cli()
    {
      return await _cliContainerDriver.ListAsync(_cliContext);
    }

    [BenchmarkCategory("ContainerList"), Benchmark]
    public async Task<Model.Drivers.CommandResponse<IList<Container>>> ListContainers_Api()
    {
      return await _apiContainerDriver.ListAsync(_apiContext);
    }

    // --- ImageList ---

    [BenchmarkCategory("ImageList"), Benchmark(Baseline = true)]
    public async Task<Model.Drivers.CommandResponse<IList<Image>>> ListImages_Cli()
    {
      return await _cliImageDriver.ListAsync(_cliContext);
    }

    [BenchmarkCategory("ImageList"), Benchmark]
    public async Task<Model.Drivers.CommandResponse<IList<Image>>> ListImages_Api()
    {
      return await _apiImageDriver.ListAsync(_apiContext);
    }

    // --- SystemInfo ---

    [BenchmarkCategory("SystemInfo"), Benchmark(Baseline = true)]
    public async Task<CommandResponse<SystemInfo>> SystemInfo_Cli()
    {
      return await _cliSystemDriver.GetInfoAsync(_cliContext);
    }

    [BenchmarkCategory("SystemInfo"), Benchmark]
    public async Task<CommandResponse<SystemInfo>> SystemInfo_Api()
    {
      return await _apiSystemDriver.GetInfoAsync(_apiContext);
    }

    // --- SystemVersion ---

    [BenchmarkCategory("SystemVersion"), Benchmark(Baseline = true)]
    public async Task<CommandResponse<VersionInfo>> SystemVersion_Cli()
    {
      return await _cliSystemDriver.GetVersionAsync(_cliContext);
    }

    [BenchmarkCategory("SystemVersion"), Benchmark]
    public async Task<CommandResponse<VersionInfo>> SystemVersion_Api()
    {
      return await _apiSystemDriver.GetVersionAsync(_apiContext);
    }
  }
}
