using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  public sealed partial class DockerCliProductionReadinessTests
  {
    [Fact]
    public async Task ServiceList_QuietStillParsesJsonAndAppliesFilters()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"service-list-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliServiceDriver(new FakeResolver(CreateFakeDocker($$"""
#!/bin/sh
printf '%s\n' "$@" > "{{record}}"
printf '%s\n' '{"ID":"svc1","Name":"web","Mode":"replicated","Replicas":"1/1","Image":"nginx"}'
printf '%s\n' '{"ID":"svc2","Name":"api","Mode":"global","Replicas":"2/2","Image":"redis"}'
""")));
      driver.Initialize(new DriverContext("docker"));

      var quiet = await driver.ListAsync(new DriverContext("docker"), new ServiceListFilter
      {
        Quiet = true,
        Name = "web",
        Id = "svc",
        Mode = "replicated",
        Labels = { { "tier", "frontend" } }
      }, TestContext.Current.CancellationToken);
      var quietArgs = await ReadArgsAsync(record);

      var full = await driver.ListAsync(new DriverContext("docker"), null, TestContext.Current.CancellationToken);

      Assert.True(quiet.Success, quiet.Error);
      Assert.Equal(["svc1", "svc2"], quiet.Data.Select(s => s.Id));
      Assert.All(quiet.Data, s => Assert.Null(s.Name));
      Assert.Contains("--format", quietArgs);
      Assert.Contains("{{json .}}", quietArgs);
      Assert.Contains("name=web", quietArgs);
      Assert.Contains("id=svc", quietArgs);
      Assert.Contains("label=tier=frontend", quietArgs);
      Assert.Contains("mode=replicated", quietArgs);
      Assert.True(full.Success, full.Error);
      Assert.Equal("web", full.Data[0].Name);
    }

    [Theory]
    [InlineData("redis", "latest", "redis:latest")]
    [InlineData("redis:7", "latest", "redis:7")]
    [InlineData("redis@sha256:abc", "latest", "redis@sha256:abc")]
    [InlineData("registry:5000/redis", "latest", "registry:5000/redis:latest")]
    [InlineData("registry:5000/redis:7", "latest", "registry:5000/redis:7")]
    public async Task ImagePull_AppendsDefaultTagOnlyForBareNames(string image, string tag, string expected)
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"pull-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliImageDriver(new FakeResolver(CreateRecordingDocker(record, "")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.PullAsync(
          new DriverContext("docker"),
          image,
          tag,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["pull", expected], await ReadArgsAsync(record));
    }

    [Fact]
    public async Task ComposeCopy_RejectsLeadingDashSourceAndDestination()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateRecordingDocker(
          Path.Combine(TestOutputDirectory(), $"compose-cp-{Guid.NewGuid():N}.txt"),
          "ok")));
      driver.Initialize(new DriverContext("docker"));

      var source = await driver.CopyAsync(new DriverContext("docker"), new ComposeCopyConfig
      {
        Source = "--help",
        Destination = "svc:/data"
      }, TestContext.Current.CancellationToken);
      var destination = await driver.CopyAsync(new DriverContext("docker"), new ComposeCopyConfig
      {
        Source = "svc:/data",
        Destination = "--help"
      }, TestContext.Current.CancellationToken);

      Assert.False(source.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, source.ErrorCode);
      Assert.False(destination.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, destination.ErrorCode);
    }

    [Fact]
    public async Task ComposeCopy_DoesNotUseBufferedTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
sleep 1
echo copied
""")));
      var context = new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(100) };
      driver.Initialize(context);

      var result = await driver.CopyAsync(context, new ComposeCopyConfig
      {
        Source = "svc:/data",
        Destination = ".out/copied"
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task ImageBuild_RejectsLeadingDashContext()
    {
      var driver = new DockerCliImageDriver(new FakeResolver("/bin/docker"));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.BuildAsync(new DriverContext("docker"), new ImageBuildConfig
      {
        BuildContext = "--help"
      }, cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public async Task StackFilters_ArePassedToCli()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"stack-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliStackDriver(new FakeResolver(CreateFakeDocker($$"""
#!/bin/sh
printf '%s\n' "$@" > "{{record}}"
case "$*" in
  *"stack ls"*) printf '%s\n' '{"Name":"demo","Services":1,"Orchestrator":"swarm","Namespace":"ns"}' ;;
  *"stack ps"*) printf '%s\n' '{"ID":"task1","Name":"demo_web.1","Image":"nginx","Node":"node1","DesiredState":"Running","CurrentState":"Running"}' ;;
  *"stack services"*) printf '%s\n' '{"ID":"svc1","Name":"demo_web","Mode":"replicated","Replicas":"1/1","Image":"nginx"}' ;;
esac
""")));
      driver.Initialize(new DriverContext("docker"));
      var context = new DriverContext("docker");

      var stacks = await driver.ListAsync(context, new StackListFilter
      {
        Orchestrator = "kubernetes",
        Namespace = "ns",
        AllNamespaces = true,
        KubeConfig = ".out/kubeconfig"
      }, TestContext.Current.CancellationToken);
      var listArgs = await ReadArgsAsync(record);

      var tasks = await driver.GetTasksAsync(context, "demo", new StackTaskFilter
      {
        Id = "task1",
        Name = "web",
        Node = "node1",
        DesiredState = "running",
        NoTrunc = true,
        NoResolve = true,
        Quiet = true,
        Orchestrator = "swarm",
        Namespace = "ns",
        KubeConfig = ".out/kubeconfig"
      }, TestContext.Current.CancellationToken);
      var taskArgs = await ReadArgsAsync(record);

      var services = await driver.GetServicesAsync(context, "demo", new StackServiceFilter
      {
        Id = "svc1",
        Name = "web",
        Labels = { { "tier", "frontend" } },
        Quiet = true,
        Orchestrator = "swarm",
        Namespace = "ns",
        KubeConfig = ".out/kubeconfig"
      }, TestContext.Current.CancellationToken);
      var serviceArgs = await ReadArgsAsync(record);

      Assert.True(stacks.Success, stacks.Error);
      // Kubernetes-orchestrator flags were removed from modern Docker; setting the (retained)
      // filter fields must not inject flags the CLI would reject with "unknown flag" (exit 125).
      Assert.DoesNotContain("--orchestrator", listArgs);
      Assert.DoesNotContain("--namespace", listArgs);
      Assert.DoesNotContain("--all-namespaces", listArgs);
      Assert.DoesNotContain("--kubeconfig", listArgs);

      Assert.True(tasks.Success, tasks.Error);
      Assert.Equal("task1", Assert.Single(tasks.Data).Id);
      Assert.Null(tasks.Data[0].Name);
      Assert.Contains("id=task1", taskArgs);
      Assert.Contains("name=web", taskArgs);
      Assert.Contains("node=node1", taskArgs);
      Assert.Contains("desired-state=running", taskArgs);
      Assert.Contains("--no-trunc", taskArgs);
      Assert.Contains("--no-resolve", taskArgs);
      Assert.DoesNotContain("--orchestrator", taskArgs);
      Assert.DoesNotContain("--namespace", taskArgs);
      Assert.DoesNotContain("--kubeconfig", taskArgs);

      Assert.True(services.Success, services.Error);
      Assert.Equal("svc1", Assert.Single(services.Data).Id);
      Assert.Null(services.Data[0].Name);
      Assert.Contains("id=svc1", serviceArgs);
      Assert.Contains("name=web", serviceArgs);
      Assert.Contains("label=tier=frontend", serviceArgs);
      Assert.DoesNotContain("--orchestrator", serviceArgs);
      Assert.DoesNotContain("--namespace", serviceArgs);
      Assert.DoesNotContain("--kubeconfig", serviceArgs);
    }

    [Fact]
    public async Task NetworkInspect_EmptyResultFailsAsNotFound()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliNetworkDriver(new FakeResolver(CreateRecordingDocker(
          Path.Combine(TestOutputDirectory(), $"network-{Guid.NewGuid():N}.txt"),
          "[]")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.InspectAsync(new DriverContext("docker"), "missing-net", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Network.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task TopAsync_PreservesSpacesInLastColumn()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
printf '%s\n' 'PID USER CMD'
printf '%s\n' '1 root nginx: master process nginx -g daemon off;'
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.TopAsync(new DriverContext("docker"), "ctr", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["PID", "USER", "CMD"], result.Data.Titles);
      Assert.Equal(["1", "root", "nginx: master process nginx -g daemon off;"], result.Data.Processes[0]);
    }

    [Fact]
    public async Task ModelLargeOperations_DoNotUseBufferedTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
sleep 1
exit 0
""");
      var context = new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(100) };
      var management = new DockerCliModelManagementDriver(new FakeResolver(docker));
      var runtime = new DockerCliModelRuntimeDriver(new FakeResolver(docker));
      management.Initialize(context);
      runtime.Initialize(context);

      var push = await management.PushAsync(context, ModelReference.Parse("ai/smollm2"), TestContext.Current.CancellationToken);
      var package = await management.PackageAsync(context, new ModelPackageRequest
      {
        Target = ModelReference.Parse("ai/smollm2")
      }, TestContext.Current.CancellationToken);
      var install = await runtime.InstallRunnerAsync(context, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(push.Success, push.Error);
      Assert.True(package.Success, package.Error);
      Assert.True(install.Success, install.Error);
    }

    [Fact]
    public async Task StreamDrivers_LogMalformedLinesAtWarning()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var sink = new LevelLoggerProvider();
      using var factory = new LevelLoggerFactory(sink);
      var driver = new DockerCliStreamDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
printf '%s\n' 'not-json'
""")));
      driver.Initialize(new DriverContext("docker") { LoggerFactory = factory });

      await foreach (var _ in driver.StreamEventsAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken))
      {
      }

      Assert.Contains(sink.Entries, e => e.Level == LogLevel.Warning);
    }

    private sealed class LevelLoggerFactory(LevelLoggerProvider provider) : ILoggerFactory
    {
      public void AddProvider(ILoggerProvider provider)
      {
      }

      public ILogger CreateLogger(string categoryName) => provider.CreateLogger(categoryName);

      public void Dispose()
      {
      }
    }

    private sealed class LevelLoggerProvider : ILoggerProvider
    {
      public ConcurrentBag<(LogLevel Level, string Message)> Entries { get; } = [];

      public ILogger CreateLogger(string categoryName) => new LevelLogger(Entries);

      public void Dispose()
      {
      }
    }

    private sealed class LevelLogger(ConcurrentBag<(LogLevel Level, string Message)> entries) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => LevelNullScope.Instance;

      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(
          LogLevel logLevel,
          EventId eventId,
          TState state,
          Exception exception,
          Func<TState, Exception, string> formatter) =>
        entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class LevelNullScope : IDisposable
    {
      public static readonly LevelNullScope Instance = new();

      public void Dispose()
      {
      }
    }
  }
}
