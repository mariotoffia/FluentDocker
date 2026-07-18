using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Extensions;
using FluentDocker.Kernel;
using FluentDocker.Model.Builders.FileBuilder;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Networks;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public sealed class BuilderChunk6ProdReadinessTests : MockKernelTestBase, IAsyncLifetime
  {
    public ValueTask InitializeAsync() => new(InitializeMockKernelAsync());

    [Fact]
    public async Task UseCompose_PreExistingProjectFailure_DoesNotRunDown()
    {
      MockPack
          .SetupComposeList(new ComposeServiceInfo { Name = "web", Project = "shared" })
          .SetupComposeDown();
      MockPack.ComposeDriver
          .Setup(d => d.UpAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeUpConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ComposeUpResult>.Fail("boom"));

      await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithProjectName("shared").WithRemoveVolumes(true))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      MockPack.ComposeDriver.Verify(d => d.ListAsync(
          It.IsAny<DriverContext>(),
          It.Is<ComposeListConfig>(cfg => cfg.ProjectName == "shared" && cfg.All),
          It.IsAny<CancellationToken>()), Times.Once);
      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UseCompose_PreExistingProjectCancellation_DoesNotRunDown()
    {
      MockPack
          .SetupComposeList(new ComposeServiceInfo { Name = "web", Project = "shared" })
          .SetupComposeDown();
      MockPack.ComposeDriver
          .Setup(d => d.UpAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeUpConfig>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());

      await Assert.ThrowsAsync<OperationCanceledException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithProjectName("shared").WithRemoveVolumes(true))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UseCompose_WithEnvFile_DoesNotOverrideHostEnvironmentAndStripsInlineComments()
    {
      Directory.CreateDirectory(".out");
      var key = "FLUENTDOCKER_CHUNK6_" + Guid.NewGuid().ToString("N");
      var envFile = Path.Combine(".out", $"{key}.env");
      var prior = Environment.GetEnvironmentVariable(key);
      Environment.SetEnvironmentVariable(key, "host-value");
      await File.WriteAllTextAsync(
          envFile,
          $"{key}=file-value\nCOMMENTED=value # trailing comment\nQUOTED=\"value # literal\"\n",
          TestContext.Current.CancellationToken);
      try
      {
        MockPack.SetupComposeList().SetupComposeUp("env-project");

        await using var results = await new Builder()
            .WithinDriver(DriverId, Kernel)
            .UseCompose(c => c.WithEnvFile(envFile))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        MockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(cfg =>
                !cfg.Environment.ContainsKey(key) &&
                cfg.Environment["COMMENTED"] == "value" &&
                cfg.Environment["QUOTED"] == "value # literal"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        Environment.SetEnvironmentVariable(key, prior);
        File.Delete(envFile);
      }
    }

    [Fact]
    public async Task UseCompose_WithEnvFile_PreservesHashAfterEscapedQuoteInQuotedValue()
    {
      Directory.CreateDirectory(".out");
      var envFile = Path.Combine(".out", "BuilderChunk6Escaped.env");
      await File.WriteAllTextAsync(
          envFile,
          "ESCAPED=\"value \\\" # literal\"\n",
          TestContext.Current.CancellationToken);
      try
      {
        MockPack.SetupComposeList().SetupComposeUp("env-project");

        await using var results = await new Builder()
            .WithinDriver(DriverId, Kernel)
            .UseCompose(c => c.WithEnvFile(envFile))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        // BF-11 (compose-go/godotenv parity): the escaped quote inside the double-quoted value is
        // unescaped to '"' after quote stripping; the '#' stays literal (it is inside the quotes).
        MockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(cfg => cfg.Environment["ESCAPED"] == "value \" # literal"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        File.Delete(envFile);
      }
    }

    [Theory]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("ftps://example.com/file.txt")]
    public void DockerfileCopy_FtpUrl_ThrowsFluentDockerException(string url)
    {
      var ex = Assert.Throws<FluentDockerException>(() =>
          new DockerfileBuilder().Copy(url, "/app/file.txt"));

      Assert.Contains("HTTP/HTTPS", ex.Message);
    }

    [Fact]
    public void DockerfileCopy_UrlWithoutFilename_ThrowsFluentDockerException()
    {
      var ex = Assert.Throws<FluentDockerException>(() =>
          new DockerfileBuilder().Copy("https://example.com/download/", "/app/"));

      Assert.Contains("filename", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_CancelledDuringStartup_ThrowsOperationCanceledException()
    {
      using var cts = new CancellationTokenSource();
      cts.Cancel();
      MockPack
          .SetupContainerCreate("cancelled-container")
          .SetupContainerStart()
          .SetupContainerInspect("cancelled-container", running: false)
          .SetupContainerRemove();

      await Assert.ThrowsAsync<OperationCanceledException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine").WithStartupTimeout(10))
          .BuildAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task UseContainer_WithAutoRemoveAndMissingAfterStart_ReportsNameAndAutoRemove()
    {
      MockPack
          .SetupContainerCreate("gone-container")
          .SetupContainerStart()
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .SetupSequence(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "gone-container", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "gone-container",
            Name = "fast-exit",
            State = new ContainerState { Running = true, Status = "running" }
          }))
          .ReturnsAsync(CommandResponse<Container>.Fail("missing", ErrorCodes.Container.NotFound));

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithName("fast-exit")
              .WithAutoRemove()
              .WithStartupTimeout(10))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("fast-exit", ex.Message);
      Assert.Contains("AutoRemove", ex.Message);
      Assert.Contains("no longer exists", ex.Message);
    }

    [Fact]
    public async Task UseContainer_WithInspectFailureAfterStart_PreservesInspectError()
    {
      MockPack
          .SetupContainerCreate("bad-inspect")
          .SetupContainerStart()
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .SetupSequence(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "bad-inspect", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "bad-inspect",
            Name = "web",
            State = new ContainerState { Running = true, Status = "running" }
          }))
          .ReturnsAsync(CommandResponse<Container>.Fail(
              "inspect failed", ErrorCodes.Container.InspectFailed));

      var ex = await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithName("web")
              .WithStartupTimeout(10))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("inspect failed", ex.Message);
      Assert.DoesNotContain("AutoRemove", ex.Message);
    }

    [Fact]
    public async Task RetryAfterCreatedNetworkRemoved_DoesNotRemoveForeignSameNameNetwork()
    {
      var listCalls = 0;
      MockPack.NetworkDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(), null!, It.IsAny<CancellationToken>()))
          .ReturnsAsync(() => CommandResponse<IList<Network>>.Ok(
              listCalls++ == 0 ? [] : [new Network { Id = "foreign-net", Name = "retry-net" }]));
      MockPack
          .SetupNetworkCreate("created-net")
          .SetupNetworkRemove();
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Fail("boom"));
      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n.WithName("retry-net"))
          .UseContainer(c => c.UseImage("alpine"));

      await Assert.ThrowsAsync<DriverException>(() =>
          builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      await Assert.ThrowsAsync<DriverException>(() =>
          builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      MockPack.NetworkDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [Fact]
    public void UseContainer_WithNullEnvironmentString_ThrowsArgumentNullException()
    {
      Assert.Throws<ArgumentNullException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.WithEnvironment(null!)));
    }

    [Fact]
    public void UseContainer_WithNullHostPort_ThrowsArgumentNullException()
    {
      Assert.Throws<ArgumentNullException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.WithPort(null!, "80")));
    }

    [Fact]
    public void PodBuilder_WithEmptyPort_ThrowsArgumentException()
    {
      Assert.Throws<ArgumentException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UsePod(p => p.WithPort("", "80")));
    }

    [Fact]
    public void WrapValue_WithNewline_ThrowsFluentDockerException()
    {
      var ex = Assert.Throws<FluentDockerException>(() =>
          new[] { new TemplateString("LABEL=line1\nRUN injected") }.WrapValue());

      Assert.Contains("control", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void ExposeCommand_WithInvalidPort_ThrowsFluentDockerException(int port)
    {
      var ex = Assert.Throws<FluentDockerException>(() => new ExposeCommand(port));

      Assert.Contains("1-65535", ex.Message);
    }

    [Theory]
    [InlineData("Bad.Project")]
    [InlineData("_bad")]
    public async Task UseCompose_WithInvalidProjectName_ThrowsFluentDockerException(string projectName)
    {
      MockPack.SetupComposeUp(projectName);

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithProjectName(projectName))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("Invalid compose project name", ex.Message);
      MockPack.ComposeDriver.Verify(d => d.UpAsync(
          It.IsAny<DriverContext>(), It.IsAny<ComposeUpConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("gateway")]
    [InlineData("ip-range")]
    public async Task UseNetwork_WithInvalidAddressing_ThrowsFluentDockerException(string field)
    {
      MockPack
          .SetupNetworkList()
          .SetupNetworkCreate();

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n =>
          {
            n.WithName("bad-net");
            if (field == "gateway")
              n.WithGateway("not-an-ip");
            else
              n.WithIPRange("not-a-cidr");
          })
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains(field == "gateway" ? "gateway" : "IP range", ex.Message);
      MockPack.NetworkDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(), It.IsAny<NetworkCreateConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DockerCliWrapper_KeepsCapturedScopeAfterRootScopeChanges()
    {
      var dockerPack = new MockDriverPack()
          .SetupContainerCreate("docker-container")
          .SetupContainerStart()
          .SetupContainerInspect("docker-container", running: true)
          .SetupContainerRemove();
      var podmanPack = new MockDriverPack()
          .SetupContainerCreate("podman-container")
          .SetupContainerStart()
          .SetupContainerInspect("podman-container", running: true)
          .SetupContainerRemove();
      await dockerPack.InitializeAsync(new DriverContext("docker"), TestContext.Current.CancellationToken);
      await podmanPack.InitializeAsync(new DriverContext("podman"), TestContext.Current.CancellationToken);
      // WithinPodmanCli now fails fast unless the scoped driver resolves the pod port (BLD-MAJ-6).
      podmanPack.RegisterCustomDriver<FluentDocker.Drivers.Podman.IPodmanPodDriver>(
          new Mock<FluentDocker.Drivers.Podman.IPodmanPodDriver>().Object);
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "docker", dockerPack, new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);
      await kernel.RegisterDriverPackAsync(
          "podman", podmanPack, new DriverContext("podman"), cancellationToken: TestContext.Current.CancellationToken);
      var builder = new Builder();
      var docker = builder.WithinDockerCli("docker", kernel);
      builder.WithinPodmanCli("podman", kernel);

      docker.UseContainer(c => c.UseImage("alpine").WithName("web"));
      await using var results = await docker.BuildAsync(
          cancellationToken: TestContext.Current.CancellationToken);

      dockerPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(),
          It.IsAny<CancellationToken>()), Times.Once);
      podmanPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReuseIfExists_StartsStoppedBorrowedContainerButDisposeDoesNotStopIt()
    {
      MockPack
          .SetupContainerList(new Container { Id = "borrowed-id", Name = "borrowed" })
          .SetupContainerStart()
          .SetupContainerStop();
      MockPack.ContainerDriver
          .SetupSequence(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "borrowed-id", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "borrowed-id",
            Name = "borrowed",
            State = new ContainerState { Running = false, Status = "exited" }
          }))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "borrowed-id",
            Name = "borrowed",
            State = new ContainerState { Running = true, Status = "running" }
          }));

      await using (var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine").WithName("borrowed").ReuseIfExists())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken))
      {
      }

      MockPack.ContainerDriver.Verify(d => d.StartAsync(
          It.IsAny<DriverContext>(), "borrowed-id", It.IsAny<CancellationToken>()), Times.Once);
      MockPack.ContainerDriver.Verify(d => d.StopAsync(
          It.IsAny<DriverContext>(), "borrowed-id", It.IsAny<int?>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }
  }
}
