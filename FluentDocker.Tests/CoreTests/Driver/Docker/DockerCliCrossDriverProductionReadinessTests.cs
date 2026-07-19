using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for behaviour that must hold uniformly across several Docker CLI
  /// component drivers at once: real-world CreatedAt parsing, leading-dash positional rejection,
  /// per-call context propagation, positional-path preservation, buffered-timeout exclusions,
  /// invalid-JSON parse failures and combined volume/system/image findings.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliCrossDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task DockerCliJsonFixtures_ParseRealCreatedAtFormats()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      // Fixture fidelity: captured from Docker 29.5.3 on 2026-07-03 with
      // `docker ps/images/history --format "{{json .}}"`; tests never require Docker.
      var driver = CreateFakeDocker("""
#!/bin/sh
case "$1" in
  ps) printf '%s\n' '{"Command":"\"sleep 300\"","CreatedAt":"2026-07-03 15:07:49 +0200 CEST","ID":"f33a5931b70c","Image":"alpine:latest","Names":"fdfixture","State":"running","Status":"Up Less than a second"}'; exit 0 ;;
  images) printf '%s\n' '{"Containers":"0","CreatedAt":"2026-06-22 22:53:00 +0200 CEST","ID":"54f2a904c251","Repository":"nginx","Size":"92.6MB","Tag":"alpine"}'; exit 0 ;;
  history) printf '%s\n' '{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-06-16T02:01:20+02:00","CreatedBy":"CMD [\"/bin/sh\"]","ID":"28bd5fe8b56d","Size":"0B"}'; exit 0 ;;
esac
exit 2
""");
      var container = new DockerCliContainerDriver(new FakeResolver(driver));
      var image = new DockerCliImageDriver(new FakeResolver(driver));
      container.Initialize(new DriverContext("docker"));
      image.Initialize(new DriverContext("docker"));

      var containers = await container.ListAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);
      var images = await image.ListAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);
      var history = await image.HistoryAsync(new DriverContext("docker"), "alpine:latest", TestContext.Current.CancellationToken);

      Assert.True(containers.Success, containers.Error);
      Assert.True(images.Success, images.Error);
      Assert.True(history.Success, history.Error);
      // Container keeps the engine offset; image and history still expose UTC DateTime.
      Assert.Equal(new DateTimeOffset(2026, 7, 3, 15, 7, 49, TimeSpan.FromHours(2)), Assert.Single(containers.Data).Created);
      Assert.Equal(new DateTime(2026, 6, 22, 20, 53, 0, DateTimeKind.Utc), Assert.Single(images.Data).Created);
      Assert.Equal(new DateTime(2026, 6, 16, 0, 1, 20, DateTimeKind.Utc), Assert.Single(history.Data).Created);
    }

    [Fact]
    public async Task LeadingDashPositionals_AreRejectedAcrossDockerCliDrivers()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var marker = Path.Combine(TestOutputDirectory(), $"dash-marker-{Guid.NewGuid():N}");
      var docker = CreateFakeDocker($"""
#!/bin/sh
touch '{marker}'
exit 0
""");
      var context = new DriverContext("docker");
      var image = new DockerCliImageDriver(new FakeResolver(docker));
      var compose = new DockerCliComposeDriver(new FakeResolver(docker));
      var stack = new DockerCliStackDriver(new FakeResolver(docker));
      var auth = new DockerCliAuthDriver(new FakeResolver(docker));
      image.Initialize(context);
      compose.Initialize(context);
      stack.Initialize(context);
      auth.Initialize(context);

      var failures = new List<(bool Success, string ErrorCode)>
      {
        ToStatus(await image.PullAsync(context, "-repo", "", cancellationToken: TestContext.Current.CancellationToken)),
        ToStatus(await image.PushAsync(context, "-repo", cancellationToken: TestContext.Current.CancellationToken)),
        ToStatus(await image.SaveAsync(context, ["-repo"], "out.tar", TestContext.Current.CancellationToken)),
        ToStatus(await compose.StartAsync(context, new ComposeFileConfig { Services = ["-svc"] }, TestContext.Current.CancellationToken)),
        ToStatus(await stack.DeployAsync(context, new StackDeployConfig { StackName = "-stack" }, TestContext.Current.CancellationToken)),
        ToStatus(await stack.RemoveAsync(context, ["-stack"], TestContext.Current.CancellationToken)),
        ToStatus(await auth.LoginAsync(context, new RegistryLoginConfig { Server = "-registry" }, TestContext.Current.CancellationToken)),
        ToStatus(await auth.LogoutAsync(context, "-registry", TestContext.Current.CancellationToken))
      };

      Assert.All(failures, failure =>
      {
        Assert.False(failure.Success);
        Assert.Equal(ErrorCodes.General.InvalidArgument, failure.ErrorCode);
      });
      Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task VolumeSystemAndImageFindings_ReturnProductionReadyResults()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var logs = new List<string>();
      var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new ListLoggerProvider(logs)));
      var driver = CreateFakeDocker("""
#!/bin/sh
case "$1 $2" in
  "volume inspect") echo '[]'; exit 0 ;;
  "version --format") echo 'daemon down' >&2; exit 7 ;;
  "system df") printf '%s\n' '{"Type":'; exit 0 ;;
  "images --format") printf '%s\n' '{"ID":"sha256:abc","Repository":"repo","Tag":"latest","Size":"1KiB","CreatedAt":"2026-06-22 22:53:00 +0200 CEST"}'; exit 0 ;;
  "history --format") printf '%s\n' '{"ID":"layer","CreatedAt":"2026-06-16T02:01:20+02:00","CreatedBy":"CMD","Size":"2MiB"}'; exit 0 ;;
esac
exit 2
""");
      var context = new DriverContext("docker") { LoggerFactory = loggerFactory };
      var volume = new DockerCliVolumeDriver(new FakeResolver(driver));
      var system = new DockerCliSystemDriver(new FakeResolver(driver));
      var image = new DockerCliImageDriver(new FakeResolver(driver));
      volume.Initialize(context);
      system.Initialize(context);
      image.Initialize(context);

      var missing = await volume.InspectAsync(context, "missing", TestContext.Current.CancellationToken);
      var linux = await system.IsLinuxEngineAsync(context, TestContext.Current.CancellationToken);
      var disk = await system.GetDiskUsageAsync(context, TestContext.Current.CancellationToken);
      var images = await image.ListAsync(context, cancellationToken: TestContext.Current.CancellationToken);
      var history = await image.HistoryAsync(context, "repo:latest", TestContext.Current.CancellationToken);

      Assert.False(missing.Success);
      Assert.Equal(ErrorCodes.Volume.NotFound, missing.ErrorCode);
      Assert.False(linux.Success);
      Assert.Equal(7, linux.ExitCode);
      Assert.True(disk.Success, disk.Error);
      Assert.Contains(logs, l => l.Contains("Disk usage JSON parsing failed", StringComparison.Ordinal));
      Assert.Equal(1024, Assert.Single(images.Data).Size);
      Assert.Equal(2 * 1024 * 1024, Assert.Single(history.Data).Size);
    }

    [Fact]
    public async Task PerCallContext_ReachesContainerComposeAndNetworkDrivers()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"args-{Guid.NewGuid():N}.txt");
      var docker = CreateRecordingDocker(record, "{}");
      var context = new DriverContext("docker") { Host = "tcp://per-call:2375" };

      var container = new DockerCliContainerDriver(new FakeResolver(docker));
      container.Initialize(new DriverContext("docker") { Host = "tcp://component:2375" });
      Assert.True((await container.StartAsync(context, "abc123", TestContext.Current.CancellationToken)).Success);
      Assert.Contains("tcp://per-call:2375", await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken));

      var compose = new DockerCliComposeDriver(new FakeResolver(docker));
      compose.Initialize(new DriverContext("docker") { Host = "tcp://component:2375" });
      Assert.True((await compose.ListAsync(context, new ComposeListConfig(), TestContext.Current.CancellationToken)).Success);
      Assert.Contains("tcp://per-call:2375", await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken));

      var network = new DockerCliNetworkDriver(new FakeResolver(docker));
      network.Initialize(new DriverContext("docker") { Host = "tcp://component:2375" });
      Assert.True((await network.ListAsync(context, cancellationToken: TestContext.Current.CancellationToken)).Success);
      Assert.Contains("tcp://per-call:2375", await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CopyAndImport_PositionalPaths_PreserveBackslashSpacesAndQuotes()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"args-{Guid.NewGuid():N}.txt");
      var docker = CreateRecordingDocker(record, "ok");
      var container = new DockerCliContainerDriver(new FakeResolver(docker));
      var image = new DockerCliImageDriver(new FakeResolver(docker));
      container.Initialize(new DriverContext("docker"));
      image.Initialize(new DriverContext("docker"));

      var path = "C:\\Temp\\with space\\quoted\"name\\";

      Assert.True((await container.CopyToAsync(new DriverContext("docker"), "abc123", path, "/app/data", TestContext.Current.CancellationToken)).Success);
      Assert.Equal(["cp", path, "abc123:/app/data"], await ReadArgsAsync(record));

      Assert.True((await container.CopyFromAsync(new DriverContext("docker"), "abc123", "/app/data", path, TestContext.Current.CancellationToken)).Success);
      Assert.Equal(["cp", "abc123:/app/data", path], await ReadArgsAsync(record));

      Assert.True((await container.ExportAsync(new DriverContext("docker"), "abc123", path, TestContext.Current.CancellationToken)).Success);
      Assert.Equal(["export", "-o", path, "abc123"], await ReadArgsAsync(record));

      Assert.True((await image.ImportAsync(new DriverContext("docker"), path, message: "msg with \"quote\"", cancellationToken: TestContext.Current.CancellationToken)).Success);
      Assert.Equal(["import", "-m", "msg with \"quote\"", path], await ReadArgsAsync(record));
    }

    [Fact]
    public async Task CreateAndComposeLongOperations_DoNotUseBufferedTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
sleep 1
case "$*" in
  *"compose up"*) echo up; exit 0 ;;
  *"compose run"*) echo run; exit 0 ;;
  *"compose exec"*) echo exec; exit 0 ;;
  create*) echo abc123; exit 0 ;;
esac
exit 2
""");
      var context = new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(100) };

      var container = new DockerCliContainerDriver(new FakeResolver(docker));
      container.Initialize(context);
      Assert.True((await container.CreateAsync(context, new ContainerCreateConfig { Image = "alpine" }, TestContext.Current.CancellationToken)).Success);

      var compose = new DockerCliComposeDriver(new FakeResolver(docker));
      compose.Initialize(context);
      Assert.True((await compose.UpAsync(context, new ComposeUpConfig(), TestContext.Current.CancellationToken)).Success);
      Assert.True((await compose.RunAsync(context, new ComposeRunConfig { Service = "web" }, TestContext.Current.CancellationToken)).Success);
      Assert.True((await compose.ExecuteAsync(context, new ComposeExecConfig { Service = "web", Command = ["true"] }, TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task InspectDrivers_InvalidJson_ReturnParseFailures()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
echo not-json
exit 0
""");
      var context = new DriverContext("docker");
      var container = new DockerCliContainerDriver(new FakeResolver(docker));
      var image = new DockerCliImageDriver(new FakeResolver(docker));
      var network = new DockerCliNetworkDriver(new FakeResolver(docker));
      var volume = new DockerCliVolumeDriver(new FakeResolver(docker));
      container.Initialize(context);
      image.Initialize(context);
      network.Initialize(context);
      volume.Initialize(context);

      var containerResult = await container.InspectAsync(context, "ctr", TestContext.Current.CancellationToken);
      var imageResult = await image.InspectAsync(context, "img", TestContext.Current.CancellationToken);
      var networkResult = await network.InspectAsync(context, "net", TestContext.Current.CancellationToken);
      var volumeResult = await volume.InspectAsync(context, "vol", TestContext.Current.CancellationToken);

      Assert.False(containerResult.Success);
      Assert.False(imageResult.Success);
      Assert.False(networkResult.Success);
      Assert.False(volumeResult.Success);
      Assert.Contains("Container inspect JSON parsing failed", containerResult.Error);
      Assert.Contains("Image inspect JSON parsing failed", imageResult.Error);
      Assert.Contains("Network inspect JSON parsing failed", networkResult.Error);
      Assert.Contains("Volume inspect JSON parsing failed", volumeResult.Error);
    }

    [Fact]
    public async Task ContainerAndImageList_AllJsonLinesFail_ReturnFailure()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
echo not-json
exit 0
""");
      var container = new DockerCliContainerDriver(new FakeResolver(docker));
      var image = new DockerCliImageDriver(new FakeResolver(docker));
      container.Initialize(new DriverContext("docker"));
      image.Initialize(new DriverContext("docker"));

      var containers = await container.ListAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);
      var images = await image.ListAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(containers.Success);
      Assert.False(images.Success);
    }

    [Fact]
    public async Task LeadingDashServiceScaleTagAndImportPositionals_FailBeforeDocker()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
echo should-not-run
exit 0
""");
      var service = new DockerCliServiceDriver(new FakeResolver(docker));
      var image = new DockerCliImageDriver(new FakeResolver(docker));
      service.Initialize(new DriverContext("docker"));
      image.Initialize(new DriverContext("docker"));

      var scale = await service.ScaleAsync(new DriverContext("docker"), new Dictionary<string, int> { { "-d", 3 } }, cancellationToken: TestContext.Current.CancellationToken);
      var tag = await image.TagAsync(new DriverContext("docker"), "sha256:abc", "-repo", "latest", TestContext.Current.CancellationToken);
      var import = await image.ImportAsync(new DriverContext("docker"), "-archive.tar", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(scale.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, scale.ErrorCode);
      Assert.False(tag.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, tag.ErrorCode);
      Assert.False(import.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, import.ErrorCode);
    }

    private static (bool Success, string ErrorCode) ToStatus<T>(CommandResponse<T> response) =>
        (response.Success, response.ErrorCode);

    private sealed class ListLoggerProvider(List<string> messages) : ILoggerProvider
    {
      public ILogger CreateLogger(string categoryName) => new ListLogger(messages);
      public void Dispose() { }
    }

    private sealed class ListLogger(List<string> messages) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
          messages.Add(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new();
      public void Dispose() { }
    }
  }
}
