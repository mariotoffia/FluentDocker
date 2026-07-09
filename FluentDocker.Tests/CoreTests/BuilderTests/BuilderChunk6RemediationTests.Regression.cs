using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Networks;
using FluentDocker.Model.Volumes;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public partial class BuilderChunk6RemediationTests
  {
    [Fact]
    public async Task WaitForHttpUrl_PositiveContinuationDelay_DoesNotAlsoWaitPollInterval()
    {
      const int targetAttempts = 20;
      var attempts = 0;
      using var listener = StartHttpListener(out var url);
      var server = RespondUntilStoppedAsync(listener, TestContext.Current.CancellationToken);
      MockPack
          .SetupContainerCreate("http-url-container")
          .SetupContainerStart()
          .SetupContainerInspect("http-url-container", running: true)
          .SetupContainerRemove()
          .SetupContainerGetLogs("");

      // Poll interval is a full 5 s; the continuation asks for a 1 ms delay each poll and
      // succeeds (-1) only once 20 polls have happened. If a positive continuation delay
      // ALSO waited the poll interval, the second poll alone would blow past the 10 s
      // budget and the build would time out at ~2 attempts. Correct behaviour runs all 20
      // rapidly. This is decoupled from HTTP round-trip latency, so it is load-independent.
      var sw = Stopwatch.StartNew();
      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx")
              .WithWaitPollInterval(5000)
              .WaitForHttpUrl(url, timeoutMs: 10000, continuation: (_, _) =>
                  ++attempts >= targetAttempts ? -1 : 1))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
      sw.Stop();
      listener.Stop();
      await server.ConfigureAwait(false);

      Assert.Equal(targetAttempts, attempts);
      Assert.True(sw.ElapsedMilliseconds < 4000,
          $"{targetAttempts} one-ms-delay polls took {sw.ElapsedMilliseconds} ms; the 5 s poll interval must not be waited between them.");
    }

    [Fact]
    public async Task BuildAsync_BorrowedNetworkAndVolumeWithRemoveOnDispose_LogsWarning()
    {
      var loggerFactory = new CapturingLoggerFactory();
      var pack = new MockDriverPack();
      await pack.InitializeAsync(new DriverContext("docker"));
      var kernel = new FluentDockerKernel(new DriverRegistry(loggerFactory), loggerFactory);
      await kernel.RegisterDriverPackAsync("docker", pack, new DriverContext("docker"));
      pack.SetupNetworkList(new Network { Id = "net-id", Name = "shared-net" });
      pack.VolumeDriver
          .Setup(d => d.InspectAsync(It.IsAny<DriverContext>(), "shared-vol", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Volume>.Ok(new Volume { Name = "shared-vol", Driver = "local" }));

      await using var results = await new Builder()
          .WithinDriver("docker", kernel)
          .UseNetwork(n => n.WithName("shared-net").RemoveOnDispose())
          .UseVolume(v => v.WithName("shared-vol").RemoveOnDispose())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Contains(loggerFactory.Messages, m => m.Contains("Network 'shared-net' already exists", StringComparison.Ordinal));
      Assert.Contains(loggerFactory.Messages, m => m.Contains("Volume 'shared-vol' already exists", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsync_RetryReusingPriorAttemptNetworkLeftover_LogsWarning()
    {
      var loggerFactory = new CapturingLoggerFactory();
      var pack = new MockDriverPack();
      await pack.InitializeAsync(new DriverContext("docker"));
      var kernel = new FluentDockerKernel(new DriverRegistry(loggerFactory), loggerFactory);
      await kernel.RegisterDriverPackAsync("docker", pack, new DriverContext("docker"));
      var lists = 0;
      pack.NetworkDriver
          .Setup(d => d.ListAsync(It.IsAny<DriverContext>(), null, It.IsAny<CancellationToken>()))
          .ReturnsAsync(() => CommandResponse<IList<Network>>.Ok(lists++ == 0
              ? []
              : [new Network { Id = "net-id", Name = "retry-net" }]));
      pack.SetupNetworkCreate("retry-net");
      pack.ContainerDriver
          .Setup(d => d.CreateAsync(It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Fail("boom"));

      var builder = new Builder()
          .WithinDriver("docker", kernel)
          .UseNetwork(n => n.WithName("retry-net"))
          .UseContainer(c => c.UseImage("alpine"));
      await Assert.ThrowsAsync<DriverException>(() => builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      await Assert.ThrowsAsync<DriverException>(() => builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains(loggerFactory.Messages, m => m.Contains("left over from a prior failed build attempt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsync_ContainerBeforeDeclaredImage_ThrowsOrderingError()
    {
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("app").WithName("web"))
          .UseImage("app", d => d.FromString("FROM scratch"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("references image 'app' which is declared after it", ex.Message);
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildAsync_ContainerBeforeDeclaredPod_ThrowsOrderingError()
    {
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine").WithName("web").WithPod("app-pod"))
          .UsePod(p => p.WithName("app-pod"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("references pod 'app-pod' which is declared after it", ex.Message);
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildAsync_InterleavedDriverScopes_ThrowsContiguousScopeError()
    {
      var (kernel, dockerPack, podmanPack) = await CreateTwoScopeKernelAsync(TestContext.Current.CancellationToken);
      await using (kernel)
      {
        dockerPack
            .SetupContainerCreate("docker-container")
            .SetupContainerStart()
            .SetupContainerInspect("docker-container", running: true)
            .SetupContainerRemove();
        podmanPack
            .SetupContainerCreate("podman-container")
            .SetupContainerStart()
            .SetupContainerInspect("podman-container", running: true)
            .SetupContainerRemove();

        var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("alpine").WithName("docker-a"))
            .WithinDriver("podman", kernel)
            .UseContainer(c => c.UseImage("alpine").WithName("podman-b"))
            .WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("alpine").WithName("docker-c"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("operations for driver scope 'docker' are not contiguous", ex.Message);
        dockerPack.ContainerDriver.Verify(d => d.CreateAsync(
            It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(),
            It.IsAny<CancellationToken>()), Times.Never);
        podmanPack.ContainerDriver.Verify(d => d.CreateAsync(
            It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(),
            It.IsAny<CancellationToken>()), Times.Never);
      }
    }

    [Fact]
    public async Task BuildAsync_ContiguousDriverScopes_BuildsEachScope()
    {
      var (kernel, dockerPack, podmanPack) = await CreateTwoScopeKernelAsync(TestContext.Current.CancellationToken);
      await using (kernel)
      {
        dockerPack
            .SetupContainerCreate("docker-container")
            .SetupContainerStart()
            .SetupContainerInspect("docker-container", running: true)
            .SetupContainerRemove();
        podmanPack
            .SetupContainerCreate("podman-container")
            .SetupContainerStart()
            .SetupContainerInspect("podman-container", running: true)
            .SetupContainerRemove();

        await using var results = await new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("alpine").WithName("docker-a"))
            .UseContainer(c => c.UseImage("alpine").WithName("docker-c"))
            .WithinDriver("podman", kernel)
            .UseContainer(c => c.UseImage("alpine").WithName("podman-b"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, results.All.Count);
      }
    }

    [Fact]
    public async Task BuildAsync_BorrowedComposeFailure_AddsKeptManifestResource()
    {
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Fail("boom"));

      var ex = await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithProjectName("borrowed-project").ConnectToExisting())
          .UseContainer(c => c.UseImage("alpine"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      var manifest = Assert.IsType<BuildFailureManifest>(ex.Data["BuildFailureManifest"]);

      Assert.Contains(manifest.KeptResources, r =>
          r.Kind == "compose" && r.Name == "borrowed-project" && r.Reason == "borrowed");
    }

    [Fact]
    public async Task UseContainer_WithStartupTimeout_UsesExplicitBudget()
    {
      MockPack
          .SetupContainerCreate("slow-container")
          .SetupContainerStart()
          .SetupContainerRemove()
          .SetupContainerGetLogs("");
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(It.IsAny<DriverContext>(), "slow-container", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "slow-container",
            State = new ContainerState { Running = false, Status = "created" }
          }));
      var sw = Stopwatch.StartNew();

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine").WithStartupTimeout(50))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("Timeout waiting for container slow-container to start", ex.Message);
      Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UseContainer_WithStartupTimeoutNonPositive_Throws(int milliseconds)
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine").WithStartupTimeout(milliseconds)));
    }

    [Fact]
    public async Task UseContainer_DuplicateContainerPortProtocolCase_Throws()
    {
      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx")
              .WithPort("8080", "80/TCP")
              .WithPort("9090", "80/tcp"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("Duplicate container port mapping for '80/tcp'", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void WithinDriver_NullOrWhitespaceDriverId_Throws(string driverId)
    {
      Assert.IsAssignableFrom<ArgumentException>(
          Record.Exception(() => new Builder().WithinDriver(driverId!, Kernel)));
    }

    [Fact]
    public void UseCompose_WithWaitTimeoutNegative_Throws()
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithWaitTimeout(-5)));
    }

    [Fact]
    public async Task UseCompose_WithEnvFile_TrimsKeysAndKeepsExplicitEnvironmentPrecedence()
    {
      var envFile = Path.Combine(AppContext.BaseDirectory, "BuilderChunk6RemediationTests.env");
      await File.WriteAllTextAsync(envFile, "KEY = file-value\nOTHER = value\n", TestContext.Current.CancellationToken)
          .ConfigureAwait(false);
      try
      {
        MockPack.SetupComposeUp("env-project");

        await using var results = await new Builder()
            .WithinDriver(DriverId, Kernel)
            .UseCompose(c => c
                .WithEnvFile(envFile)
                .WithEnvironment("KEY", "explicit"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        MockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(cfg =>
                cfg.Environment["KEY"] == "explicit" &&
                cfg.Environment["OTHER"] == "value" &&
                !cfg.Environment.ContainsKey("KEY ")),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        File.Delete(envFile);
      }
    }

    private static HttpListener StartHttpListener(out string url)
    {
      var listener = LoopbackHttpListenerSupport.Start(out var baseUrl);
      url = baseUrl + "health";
      return listener;
    }

    private static async Task RespondUntilStoppedAsync(HttpListener listener, CancellationToken cancellationToken)
    {
      try
      {
        while (!cancellationToken.IsCancellationRequested && listener.IsListening)
        {
          var context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
          context.Response.StatusCode = 503;
          context.Response.Close();
        }
      }
      catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException)
      {
      }
    }

    private static async Task<(FluentDockerKernel Kernel, MockDriverPack DockerPack, MockDriverPack PodmanPack)> CreateTwoScopeKernelAsync(
        CancellationToken cancellationToken)
    {
      var dockerPack = new MockDriverPack();
      var podmanPack = new MockDriverPack();
      await dockerPack.InitializeAsync(new DriverContext("docker"), cancellationToken);
      await podmanPack.InitializeAsync(new DriverContext("podman"), cancellationToken);
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync("docker", dockerPack, new DriverContext("docker"), cancellationToken: cancellationToken);
      await kernel.RegisterDriverPackAsync("podman", podmanPack, new DriverContext("podman"), cancellationToken: cancellationToken);
      return (kernel, dockerPack, podmanPack);
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
      private readonly ConcurrentQueue<string> _messages = [];

      public IReadOnlyCollection<string> Messages => [.. _messages];

      public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

      public void AddProvider(ILoggerProvider provider) { }

      public void Dispose() { }
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(
          LogLevel logLevel, EventId eventId, TState state, Exception exception,
          Func<TState, Exception, string> formatter)
      {
        if (logLevel >= LogLevel.Warning)
          messages.Enqueue(formatter(state, exception));
      }
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new NullScope();
      public void Dispose() { }
    }
  }
}
