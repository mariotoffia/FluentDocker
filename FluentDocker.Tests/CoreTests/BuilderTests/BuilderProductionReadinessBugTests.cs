using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public sealed class BuilderProductionReadinessBugTests
  {
    private const string DriverId = "docker";

    [Fact]
    public async Task DockerfileAdd_RelativeSourcesWithSameBasename_StagesDistinctFilesAndKeepsPaths()
    {
      var root = NewOutDir("bld1-add-collision");
      try
      {
        var first = Path.Combine(root, "a", "app.conf");
        var second = Path.Combine(root, "b", "app.conf");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        await File.WriteAllTextAsync(first, "alpha", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(second, "bravo", TestContext.Current.CancellationToken);

        var firstSource = DockerPath(Path.GetRelativePath(Directory.GetCurrentDirectory(), first));
        var secondSource = DockerPath(Path.GetRelativePath(Directory.GetCurrentDirectory(), second));
        var workingFolder = Path.Combine(root, "context");

        var dockerfile = await new DockerfileBuilder()
            .WorkingFolder(workingFolder)
            .UseParent("alpine")
            .Add(firstSource, "/etc/a.conf")
            .Add(secondSource, "/etc/b.conf")
            .ToDockerfileStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains($@"ADD [""{firstSource}"", ""/etc/a.conf""]", dockerfile);
        Assert.Contains($@"ADD [""{secondSource}"", ""/etc/b.conf""]", dockerfile);
        Assert.Equal("alpha", await File.ReadAllTextAsync(
            StagedPath(workingFolder, firstSource), TestContext.Current.CancellationToken));
        Assert.Equal("bravo", await File.ReadAllTextAsync(
            StagedPath(workingFolder, secondSource), TestContext.Current.CancellationToken));
      }
      finally
      {
        SafeDelete(root);
      }
    }

    [Fact]
    public async Task DockerfileAdd_RootedSourcesWithSameBasename_StillThrowsCollision()
    {
      var root = NewOutDir("bld1-rooted-add-collision");
      try
      {
        var first = Path.Combine(root, "one", "app.conf");
        var second = Path.Combine(root, "two", "app.conf");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        await File.WriteAllTextAsync(first, "alpha", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(second, "bravo", TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => new DockerfileBuilder()
            .WorkingFolder(Path.Combine(root, "context"))
            .UseParent("alpine")
            .Add(first, "/etc/a.conf")
            .Add(second, "/etc/b.conf")
            .ToDockerfileStringAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Multiple rooted COPY/ADD sources share the file name 'app.conf'", ex.Message);
      }
      finally
      {
        SafeDelete(root);
      }
    }

    [Fact]
    public async Task DockerfileAdd_RelativeSourceEscapingBuildContext_Throws()
    {
      var root = NewOutDir("bld1-add-escape");
      try
      {
        var sourceFile = Path.Combine(root, "source.txt");
        await File.WriteAllTextAsync(sourceFile, "escape", TestContext.Current.CancellationToken);
        var source = Path.Combine("..", new DirectoryInfo(Directory.GetCurrentDirectory()).Name,
            Path.GetRelativePath(Directory.GetCurrentDirectory(), sourceFile)).Replace('\\', '/');

        var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new DockerfileBuilder()
            .WorkingFolder(Path.Combine(root, "context"))
            .UseParent("alpine")
            .Add(source, "/app/source.txt")
            .ToDockerfileStringAsync(TestContext.Current.CancellationToken));

        Assert.Contains("escapes the build context", ex.Message);
      }
      finally
      {
        SafeDelete(root);
      }
    }

    [Fact]
    public async Task DockerfileAdd_RootedBasenameCollidesWithNestedStagedDirectory_ThrowsTypedError()
    {
      var root = NewOutDir("bld1-dir-collision");
      var collidingName = $"coll-{Guid.NewGuid():N}";
      // A nested relative COPY 'collidingName/inner.txt' resolves against the process CWD, so its
      // source must live there; it stages to <ctx>/collidingName/inner.txt, creating a DIRECTORY
      // at <ctx>/collidingName that a later rooted ADD's identical basename then targets.
      var nestedRelativeDir = Path.Combine(Directory.GetCurrentDirectory(), collidingName);
      try
      {
        Directory.CreateDirectory(nestedRelativeDir);
        await File.WriteAllTextAsync(
            Path.Combine(nestedRelativeDir, "inner.txt"), "nested", TestContext.Current.CancellationToken);
        var rootedSource = Path.Combine(root, collidingName);
        await File.WriteAllTextAsync(rootedSource, "rooted", TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => new DockerfileBuilder()
            .WorkingFolder(Path.Combine(root, "context"))
            .UseParent("alpine")
            .Copy($"{collidingName}/inner.txt", "/app/inner.txt")
            .Add(rootedSource, "/app/conf")
            .ToDockerfileStringAsync(TestContext.Current.CancellationToken));

        Assert.Contains("a directory already exists", ex.Message);
        Assert.Contains(collidingName, ex.Message);
      }
      finally
      {
        SafeDelete(nestedRelativeDir);
        SafeDelete(root);
      }
    }

    [Fact]
    public async Task DockerfileCopy_MissingLenientRelative_ThenRootedSameBasename_StagesWithoutSpuriousCollision()
    {
      var root = NewOutDir("bld1-lenient-claim");
      try
      {
        // Relative COPY source is never created → missing; the rooted ADD shares its basename.
        var sharedName = $"absent-{Guid.NewGuid():N}.txt";
        var rootedSource = Path.Combine(root, "src", sharedName);
        Directory.CreateDirectory(Path.GetDirectoryName(rootedSource)!);
        await File.WriteAllTextAsync(rootedSource, "real", TestContext.Current.CancellationToken);
        var workingFolder = Path.Combine(root, "context");

        // Lenient string-gen (strictCopySources=false): the missing relative COPY is skipped and
        // must NOT reserve its basename, so the real rooted ADD with the same basename still stages.
        var dockerfile = await new DockerfileBuilder()
            .WorkingFolder(workingFolder)
            .UseParent("alpine")
            .Copy(sharedName, "/app/a")
            .Add(rootedSource, "/app/b")
            .ToDockerfileStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains($@"ADD [""{sharedName}"", ""/app/b""]", dockerfile);
        Assert.Equal("real", await File.ReadAllTextAsync(
            StagedPath(workingFolder, sharedName), TestContext.Current.CancellationToken));
      }
      finally
      {
        SafeDelete(root);
      }
    }

    [Fact]
    public async Task ComposeProjectPreExistingAtUp_IsBorrowedWarnsAndDoesNotDownOnDispose()
    {
      var loggerFactory = new RecordingLoggerFactory();
      var mockPack = new MockDriverPack()
          .SetupComposeList(new ComposeServiceInfo { Name = "web", Project = "shared" })
          .SetupComposeUp("shared")
          .SetupComposeDown();
      await using var kernel = await CreateKernelAsync(mockPack, loggerFactory, TestContext.Current.CancellationToken);

      await using (var results = await new Builder()
          .WithinDriver(DriverId, kernel)
          .UseCompose(c => c.WithProjectName("shared"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken))
      {
        var compose = Assert.Single(results.ComposeServices);
        Assert.True(compose.IsBorrowed);
        Assert.Contains(loggerFactory.Records, r =>
            r.Level == LogLevel.Warning &&
            r.Message.Contains("pre-existing", StringComparison.OrdinalIgnoreCase) &&
            r.Message.Contains("borrowed", StringComparison.OrdinalIgnoreCase) &&
            r.Message.Contains("not", StringComparison.OrdinalIgnoreCase) &&
            r.Message.Contains("dispose", StringComparison.OrdinalIgnoreCase));
      }

      mockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ComposeProjectCreatedByBuilder_IsNotBorrowedAndDownsOnDispose()
    {
      var mockPack = new MockDriverPack()
          .SetupComposeList()
          .SetupComposeUp("owned")
          .SetupComposeDown();
      await using var kernel = await CreateKernelAsync(
          mockPack, new RecordingLoggerFactory(), TestContext.Current.CancellationToken);

      await using (var results = await new Builder()
          .WithinDriver(DriverId, kernel)
          .UseCompose(c => c.WithProjectName("owned"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken))
      {
        var compose = Assert.Single(results.ComposeServices);
        Assert.False(compose.IsBorrowed);
      }

      mockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(),
          It.Is<ComposeDownConfig>(c => c.ProjectName == "owned"),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContainerReuse_WithQueuedEnvironment_WarnsConfigurationIsIgnored(bool running)
    {
      var loggerFactory = new RecordingLoggerFactory();
      var mockPack = new MockDriverPack()
          .SetupContainerList(new Container { Id = "existing-id", Name = "/reused" })
          .SetupContainerStart();
      mockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "existing-id", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "existing-id",
            Name = "reused",
            State = new ContainerState { Running = running, Status = running ? "running" : "exited" }
          }));
      await using var kernel = await CreateKernelAsync(mockPack, loggerFactory, TestContext.Current.CancellationToken);

      await using (await new Builder()
          .WithinDriver(DriverId, kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithName("reused")
              .WithEnvironment("APP_MODE", "prod")
              .ReuseIfExists())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken))
      {
      }

      Assert.Contains(loggerFactory.Records, r =>
          r.Level == LogLevel.Warning &&
          r.Message.Contains("reused as-is", StringComparison.OrdinalIgnoreCase) &&
          r.Message.Contains("env", StringComparison.OrdinalIgnoreCase) &&
          r.Message.Contains("ports", StringComparison.OrdinalIgnoreCase) &&
          r.Message.Contains("volumes", StringComparison.OrdinalIgnoreCase) &&
          r.Message.Contains("not applied", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ContainerReuse_WithoutQueuedConfig_DoesNotWarn()
    {
      var loggerFactory = new RecordingLoggerFactory();
      var mockPack = new MockDriverPack()
          .SetupContainerList(new Container { Id = "existing-id", Name = "/reused" })
          .SetupContainerStart();
      mockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "existing-id", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "existing-id",
            Name = "reused",
            State = new ContainerState { Running = false, Status = "exited" }
          }));
      await using var kernel = await CreateKernelAsync(mockPack, loggerFactory, TestContext.Current.CancellationToken);

      await using (await new Builder()
          .WithinDriver(DriverId, kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithName("reused")
              .ReuseIfExists())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken))
      {
      }

      Assert.DoesNotContain(loggerFactory.Records, r =>
          r.Level == LogLevel.Warning &&
          r.Message.Contains("reused as-is", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UseContainer_RestartingAfterStart_TimesOutInsteadOfSucceeding()
    {
      var mockPack = new MockDriverPack()
          .SetupContainerCreate("crash-loop")
          .SetupContainerStart()
          .SetupContainerRemove();
      mockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "crash-loop", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "crash-loop",
            Name = "web",
            State = new ContainerState
            {
              Running = true,
              Restarting = true,
              Status = "restarting"
            }
          }));
      await using var kernel = await CreateKernelAsync(
          mockPack, new RecordingLoggerFactory(), TestContext.Current.CancellationToken);

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithName("web")
              .WithStartupTimeout(10))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("Timeout waiting for container", ex.Message);
      Assert.Contains("crash-loop", ex.Message);
    }

    private static async Task<FluentDockerKernel> CreateKernelAsync(
        MockDriverPack mockPack, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
      var context = new DriverContext(DriverId);
      await mockPack.InitializeAsync(context, cancellationToken);
      var kernel = new FluentDockerKernel(new DriverRegistry(loggerFactory), loggerFactory);
      await kernel.RegisterDriverPackAsync(
          DriverId, mockPack, context, cancellationToken: cancellationToken);
      kernel.SetDefaultDriver(DriverId);
      return kernel;
    }

    private static string NewOutDir(string name)
    {
      var dir = Path.GetFullPath(Path.Combine(".out", $"{name}-{Guid.NewGuid():N}"));
      Directory.CreateDirectory(dir);
      return dir;
    }

    private static string DockerPath(string path) => path.Replace('\\', '/');

    private static string StagedPath(string workingFolder, string source) =>
        Path.Combine(workingFolder, source.Replace('/', Path.DirectorySeparatorChar));

    private static void SafeDelete(string dir)
    {
      try
      {
        if (Directory.Exists(dir))
          Directory.Delete(dir, recursive: true);
      }
      catch (IOException)
      {
      }
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
      public ConcurrentQueue<LogRecord> Records { get; } = [];
      public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Records);
      public void AddProvider(ILoggerProvider provider) { }
      public void Dispose() { }
    }

    private sealed class RecordingLogger(
        string category,
        ConcurrentQueue<LogRecord> records) : ILogger
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
        records.Enqueue(new LogRecord(logLevel, category, formatter(state, exception), exception));
      }
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new();
      public void Dispose() { }
    }

    private sealed record LogRecord(
        LogLevel Level,
        string Category,
        string Message,
        Exception? Exception);
  }
}
