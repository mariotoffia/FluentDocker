using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Builders.FileBuilder;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public sealed class BuildersProdReadyTests : MockKernelTestBase, IAsyncLifetime
  {
    public ValueTask InitializeAsync() => new(InitializeMockKernelAsync());

    [Fact]
    public void CopyCommand_RejectsInjectedChown()
    {
      var ex = Assert.Throws<FluentDockerException>(() =>
          new CopyCommand(new TemplateString("a"), new TemplateString("b"), "root\nRUN evil"));

      Assert.Contains("control characters/newlines", ex.Message);
    }

    [Fact]
    public void CopyCommand_WithValidChownAndFrom_RendersOptions()
    {
      var command = new CopyCommand(new TemplateString("a"), new TemplateString("b"), "root:root", "builder");

      Assert.Equal(@"COPY --chown=root:root --from=builder [""a"", ""b""]", command.ToString());
    }

    [Fact]
    public void ContainerBuilder_WaitForPortWithZeroTimeout_ThrowsArgumentOutOfRangeException()
    {
      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.WaitForPort("80/tcp", 0)));

      Assert.Equal("timeoutMs", ex.ParamName);
    }

    [Fact]
    public async Task DockerfileBuilder_WithBuildContextWithoutFromFile_Throws()
    {
      var context = Path.Combine(".out", "prod-ready-build-context-ignored");
      Directory.CreateDirectory(context);

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new DockerfileBuilder()
          .WithBuildContext(context)
          .UseParent("alpine")
          .ToDockerfileStringAsync(TestContext.Current.CancellationToken));

      Assert.Contains("WithBuildContext requires FromFile", ex.Message);
    }

    [Fact]
    public async Task ComposeBuilder_WhenFailureCleanupDownFails_LogsWarningAndAddsManifestEntry()
    {
      var logs = new List<LogEntry>();
      using var loggerFactory = LoggerFactory.Create(builder =>
          builder.AddProvider(new ListLoggerProvider(logs)));
      var mockPack = new MockDriverPack();
      var context = new DriverContext("docker");
      await mockPack.InitializeAsync(context, TestContext.Current.CancellationToken);
      var kernel = new FluentDockerKernel(new DriverRegistry(loggerFactory), loggerFactory);
      await kernel.RegisterDriverPackAsync("docker", mockPack, context, TestContext.Current.CancellationToken);
      kernel.SetDefaultDriver("docker");

      mockPack.SetupComposeList();
      mockPack.SetupComposeDownFailure("down failed");
      mockPack.ComposeDriver
          .Setup(d => d.UpAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeUpConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ComposeUpResult>.Fail("up failed"));

      try
      {
        var ex = await Assert.ThrowsAsync<DriverException>(() => new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("docker-compose.yml")
                .WithProjectName("failed-project"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(logs, log =>
            log.Level == LogLevel.Warning &&
            log.Message.Contains("Failed to clean up compose project", StringComparison.Ordinal));
        var manifest = Assert.IsType<BuildFailureManifest>(
            ex.Data[BuildFailureManifest.BuildFailureManifestKey]);
        Assert.Contains(manifest.KeptResources, r => r.Kind == "compose");
      }
      finally
      {
        await kernel.DisposeAsync();
      }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DockerfileExecCommands_WithMissingCommand_ThrowArgumentException(string? command)
    {
      Assert.Throws<ArgumentException>(() => new CmdCommand(command!));
      Assert.Throws<ArgumentException>(() => new EntrypointCommand(command!));
      Assert.Throws<ArgumentException>(() => new ShellCommand(command!));
    }

    [Fact]
    public void ContainerBuilder_WithLinksNull_ThrowsArgumentNullException()
    {
      string[] links = null!;

      Assert.Throws<ArgumentNullException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.WithLinks(links)));
    }

    [Fact]
    public async Task ContainerBuilder_WithSingleCharacterName_RejectedAsCrossRuntimeUnsafe()
    {
      // The Docker daemon rejects single-character names (regex requires >= 2 chars). Podman
      // accepts them, but FluentDocker validates against the stricter, cross-runtime-safe rule
      // client-side so a name accepted here is portable to both runtimes and never surfaces a
      // late daemon-side error. See PROD_READY_ISSUES.md name-regex finding.
      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithName("a"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("Invalid container name", ex.Message);
      Assert.Contains("^[a-zA-Z0-9][a-zA-Z0-9_.-]+$", ex.Message);
    }

    [Fact]
    public void ContainerBuilder_WithColonInContainerPath_ThrowsArgumentException()
    {
      var ex = Assert.Throws<ArgumentException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.WithVolume("/host", "/data:z/x")));

      Assert.Equal("containerPath", ex.ParamName);
    }

    [Fact]
    public void ContainerBuilder_WithWindowsDriveContainerPath_IsAccepted()
    {
      // A Windows-container destination like C:\app has a legitimate drive-letter colon and must be
      // accepted, mirroring the host-side Windows-drive exemption in WithVolume.
      var ex = Record.Exception(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.WithVolume(@"C:\host", @"C:\container")));

      Assert.Null(ex);
    }

    private sealed class ListLoggerProvider(List<LogEntry> entries) : ILoggerProvider
    {
      public ILogger CreateLogger(string categoryName) => new ListLogger(entries);
      public void Dispose() { }
    }

    private sealed class ListLogger(List<LogEntry> entries) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(
          LogLevel logLevel,
          EventId eventId,
          TState state,
          Exception? exception,
          Func<TState, Exception?, string> formatter)
      {
        entries.Add(new LogEntry(logLevel, formatter(state, exception)));
      }
    }

    private sealed class NullScope : IDisposable
    {
      internal static readonly NullScope Instance = new();
      public void Dispose() { }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
  }
}
