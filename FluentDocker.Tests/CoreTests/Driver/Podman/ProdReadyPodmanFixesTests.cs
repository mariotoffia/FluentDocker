using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  public sealed class ProdReadyPodmanFixesTests
  {
    [Fact]
    public void BuildImageListArgs_EmitsDockerParityFilters()
    {
      var args = PodmanCliImageDriver.BuildImageListArgs(new ImageListFilter
      {
        All = true,
        Reference = "alpine:latest",
        Dangling = true,
        Before = "old:tag",
        Since = "new:tag",
        Labels = new Dictionary<string, string>
        {
          ["stage"] = "test",
          ["empty"] = ""
        }
      });

      Assert.Contains(" -a", args);
      Assert.Contains("--filter reference=alpine:latest", args);
      Assert.Contains("--filter dangling=true", args);
      Assert.Contains("--filter before=old:tag", args);
      Assert.Contains("--filter since=new:tag", args);
      Assert.Contains("--filter label=stage=test", args);
      Assert.Contains("--filter label=empty", args);
    }

    [Fact]
    public void ParseContainerList_PodmanJson_SetsCreatedAndRunningState()
    {
      var containers = PodmanContainerParser.ParseContainerList(@"[{
        ""Id"": ""abc123"",
        ""Image"": ""alpine:latest"",
        ""Names"": [""web""],
        ""State"": ""running"",
        ""Status"": ""Up 3 seconds"",
        ""Exited"": false,
        ""Created"": ""2026-07-09T06:00:00Z"",
        ""StartedAt"": ""2026-07-09T06:01:00Z""
      }]");

      var container = Assert.Single(containers);
      Assert.Equal("abc123", container.Id);
      Assert.Equal("web", container.Name);
      Assert.Equal(DateTimeOffset.Parse("2026-07-09T06:00:00Z", CultureInfo.InvariantCulture), container.Created);
      Assert.Equal("running", container.State.Status);
      Assert.True(container.State.Running);
      Assert.Equal(DateTimeOffset.Parse("2026-07-09T06:01:00Z", CultureInfo.InvariantCulture), container.State.StartedAt);
    }

    [Fact]
    public void ParseContainerInspect_NumericStopSignal_ReturnsString()
    {
      var container = PodmanContainerParser.ParseContainerInspect(@"{""Config"":{""StopSignal"":15}}");

      Assert.Equal("15", container.Config.StopSignal);
    }

    [Fact]
    public void ParseDiskUsageOutput_ReadsIntegersWithInvariantCulture()
    {
      var priorCulture = CultureInfo.CurrentCulture;
      var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
      culture.NumberFormat.NegativeSign = "~";

      try
      {
        CultureInfo.CurrentCulture = culture;

        var info = PodmanCliSystemDriver.ParseDiskUsageOutput(
            @"[{""Type"":""Images"",""Total"":""-7"",""Active"":""-3"",""Size"":0,""Reclaimable"":0}]");

        Assert.Equal(-7, info.Images.TotalCount);
        Assert.Equal(-3, info.Images.Active);
      }
      finally
      {
        CultureInfo.CurrentCulture = priorCulture;
      }
    }

    [Fact]
    public void ExecExit125_WithoutPodmanErrorMarker_IsInfrastructureFailure()
    {
      Assert.True(PodmanCliContainerDriver.IsExecInfrastructureFailure(
          125, "", "application returned 125"));
    }

    [Fact]
    public async Task StreamingConnectionFailure_UsesMachineNotRunningClassification()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell required for deterministic streaming failure");

      var driver = CreateStreamDriver(CreateFailingPodmanScript());
      var context = new DriverContext("podman")
      {
        AutoStartMachine = new AutoStartMachineConfig()
      };

      var ex = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var evt in driver.StreamEventsAsync(
            context,
            null,
            TestContext.Current.CancellationToken))
        {
          _ = evt;
        }
      });

      Assert.Equal(ErrorCodes.Machine.NotRunning, ex.ErrorCode);
      Assert.True(ex.IsTransient);
      Assert.NotNull(ex.Context);
      Assert.Equal("StreamingCommand", ex.Context.Operation);
      Assert.Equal(2, ex.Context.ExitCode);
      Assert.Contains("Cannot connect to Podman", ex.Context.StdErr);
    }

    [Fact]
    public async Task BufferedConnectionFailure_ReturnsMachineNotRunningWithErrorContext()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell required for deterministic process fixture");

      var context = new DriverContext("podman")
      {
        AutoStartMachine = new AutoStartMachineConfig()
      };
      var driver = CreateImageDriver(CreateFailingPodmanScript(), context);

      var result = await driver.RemoveAsync(
          context,
          "alpine",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Machine.NotRunning, result.ErrorCode);
      Assert.NotNull(result.ErrorContext);
      Assert.Equal(2, result.ErrorContext.ExitCode);
      Assert.Contains("Cannot connect to Podman", result.ErrorContext.StdErr);
    }

    [Fact]
    public async Task InitializeAsync_WhenCalledTwice_ThrowsInvalidOperationException()
    {
      await using var pack = new PodmanCliDriverPack();
      // Hermetic: pack init resolves (never executes) the binary — a fake executable keeps
      // this unit test green on machines without podman (GATE-1).
      var fakeBinDir = FluentDocker.Tests.Utilities.FakeBinaryDirectory.Create("podman");

      await pack.InitializeAsync(
          new DriverContext("podman") { SearchPaths = [fakeBinDir] },
          TestContext.Current.CancellationToken);

      await Assert.ThrowsAsync<InvalidOperationException>(() => pack.InitializeAsync(
          new DriverContext("podman") { SearchPaths = [fakeBinDir] },
          TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCapabilitiesAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      var pack = new PodmanCliDriverPack();

      await pack.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          pack.GetCapabilitiesAsync(TestContext.Current.CancellationToken));
    }

    private static string CreateFailingPodmanScript()
    {
      var directory = Path.Combine(Environment.CurrentDirectory, ".out");
      Directory.CreateDirectory(directory);
      var path = Path.Combine(directory, "podman-stream-fail.sh");
      File.WriteAllText(path, "#!/bin/sh\necho 'Cannot connect to Podman socket' 1>&2\nexit 2\n");
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      return path;
    }

    private static PodmanCliStreamDriver CreateStreamDriver(string binaryPath)
    {
      var resolver = new Mock<IPodmanBinaryResolver>();
      resolver.Setup(r => r.Resolve("podman"))
          .Returns(new PodmanBinary(
              Path.GetDirectoryName(binaryPath),
              Path.GetFileName(binaryPath),
              SudoMechanism.None,
              null,
              PodmanBinaryType.PodmanClient));
      var driver = new PodmanCliStreamDriver(resolver.Object);
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliImageDriver CreateImageDriver(string binaryPath, DriverContext context)
    {
      var resolver = new Mock<IPodmanBinaryResolver>();
      resolver.Setup(r => r.Resolve("podman"))
          .Returns(new PodmanBinary(
              Path.GetDirectoryName(binaryPath),
              Path.GetFileName(binaryPath),
              SudoMechanism.None,
              null,
              PodmanBinaryType.PodmanClient));
      var driver = new PodmanCliImageDriver(resolver.Object);
      driver.Initialize(context);
      return driver;
    }
  }
}
