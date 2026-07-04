using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Kernel;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public sealed class BuilderChunk1ProdReadinessTests
  {
    [Fact]
    public async Task UsePod_WhenStartFails_RemovesPodAndRecordsManifest()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      podDriver
          .Setup(d => d.CreatePodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<PodCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<PodCreateResult>.Ok(new PodCreateResult { Id = "pod-1" }));
      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(), "leaky-pod", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("start failed", ErrorCodes.Pod.StartFailed));
      podDriver
          .Setup(d => d.RemovePodAsync(
              It.IsAny<DriverContext>(), "leaky-pod", true, It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var mockPack = new MockDriverPack();
      mockPack.RegisterCustomDriver(podDriver.Object);
      await using var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("podman", mockPack);

      var ex = await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver("podman", kernel)
          .UsePod(p => p.WithName("leaky-pod"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      var manifest = Assert.IsType<BuildFailureManifest>(ex.Data["BuildFailureManifest"]);

      podDriver.Verify(d => d.RemovePodAsync(
          It.IsAny<DriverContext>(), "leaky-pod", true, It.IsAny<CancellationToken>()),
          Times.Once);
      Assert.Contains(manifest.RemovedResources, r =>
          r.Kind == "pod" && r.Name == "leaky-pod" && r.Id == "pod-1");
    }

    [Fact]
    public async Task UsePod_WhenStartFailsAndRemoveStalls_UsesOuterCleanupTimeout()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      podDriver
          .Setup(d => d.CreatePodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<PodCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<PodCreateResult>.Ok(new PodCreateResult { Id = "pod-stall" }));
      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(), "stall-pod", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("start failed", ErrorCodes.Pod.StartFailed));
      podDriver
          .Setup(d => d.RemovePodAsync(
              It.IsAny<DriverContext>(), "stall-pod", true, It.IsAny<CancellationToken>()))
          .Returns(async (DriverContext _, string _, bool _, CancellationToken ct) =>
          {
            await Task.Delay(500, ct).ConfigureAwait(false);
            return CommandResponse<Unit>.Ok(Unit.Default);
          });

      var mockPack = new MockDriverPack();
      mockPack.RegisterCustomDriver(podDriver.Object);
      await using var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("podman", mockPack);
      var sw = Stopwatch.StartNew();

      var ex = await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver("podman", kernel)
          .UsePod(p => p.WithName("stall-pod"))
          .BuildAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken));

      Assert.True(sw.ElapsedMilliseconds < 300, $"Cleanup took {sw.ElapsedMilliseconds} ms");
      var manifest = Assert.IsType<BuildFailureManifest>(ex.Data["BuildFailureManifest"]);
      Assert.Contains(manifest.KeptResources, r =>
          r.Kind == "pod" && r.Name == "stall-pod" && r.Id == "pod-stall");
    }

    [Fact]
    public async Task UsePod_WhenLaterOperationFails_RemovesPodAndRecordsManifest()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      podDriver
          .Setup(d => d.CreatePodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<PodCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<PodCreateResult>.Ok(new PodCreateResult { Id = "pod-2" }));
      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(), "built-pod", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      podDriver
          .Setup(d => d.RemovePodAsync(
              It.IsAny<DriverContext>(), "built-pod", true, It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var mockPack = new MockDriverPack();
      mockPack.RegisterCustomDriver(podDriver.Object);
      mockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Fail("boom"));
      await using var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("podman", mockPack);

      var ex = await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver("podman", kernel)
          .UsePod(p => p.WithName("built-pod"))
          .UseContainer(c => c.UseImage("alpine"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      var manifest = Assert.IsType<BuildFailureManifest>(ex.Data["BuildFailureManifest"]);

      podDriver.Verify(d => d.RemovePodAsync(
          It.IsAny<DriverContext>(), "built-pod", true, It.IsAny<CancellationToken>()),
          Times.Once);
      Assert.Contains(manifest.RemovedResources, r =>
          r.Kind == "pod" && r.Name == "built-pod" && r.Id == "pod-2");
    }

    [Fact]
    public async Task UseImage_AfterSuccessfulBuild_DeletesGeneratedBuildContext()
    {
      var buildContext = string.Empty;
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.BuildAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageBuildConfig>(),
              It.IsAny<System.IProgress<ImageBuildProgress>>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ImageBuildConfig, System.IProgress<ImageBuildProgress>, CancellationToken>(
              (_, config, _, _) => buildContext = config.BuildContext)
          .ReturnsAsync(CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult { ImageId = "image-1" }));
      await using var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      await using var result = await new Builder()
          .WithinDriver("docker", kernel)
          .UseImage("test-image", d => d.FromString("FROM scratch"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(buildContext);
      Assert.False(Directory.Exists(buildContext));
    }

    [Fact]
    public async Task UseImage_AfterFailedBuild_DeletesGeneratedBuildContext()
    {
      var buildContext = string.Empty;
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.BuildAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageBuildConfig>(),
              It.IsAny<System.IProgress<ImageBuildProgress>>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ImageBuildConfig, System.IProgress<ImageBuildProgress>, CancellationToken>(
              (_, config, _, _) => buildContext = config.BuildContext)
          .ReturnsAsync(CommandResponse<ImageBuildResult>.Fail("build failed"));
      await using var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver("docker", kernel)
          .UseImage("test-image", d => d.FromString("FROM scratch"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.NotNull(buildContext);
      Assert.False(Directory.Exists(buildContext));
    }

    [Fact]
    public async Task UseImage_CopyMissingSource_ThrowsBeforeBuild()
    {
      var missing = Path.GetFullPath(Path.Combine(".out", "dockerfile-missing", "missing.txt"));
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
            .WithinDriver("docker", kernel)
            .UseImage("test-image", d => d.UseParent("alpine").Copy(missing, "/app/missing.txt"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains($"COPY source '{missing}' not found", ex.Message);
      }
      mockPack.ImageDriver.Verify(d => d.BuildAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<ImageBuildConfig>(),
          It.IsAny<System.IProgress<ImageBuildProgress>>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ToDockerfileStringAsync_WithExistingCopySource_DeletesOwnedTempFolder()
    {
      var source = TestEnvFilePath("copy-source.txt");
      await File.WriteAllTextAsync(source, "data", TestContext.Current.CancellationToken);
      var builder = new DockerfileBuilder()
          .UseParent("alpine")
          .Copy(source, "/app/copy-source.txt");
      var workingFolder = GetWorkingFolder(builder);

      var dockerfile = await builder.ToDockerfileStringAsync(TestContext.Current.CancellationToken);

      Assert.Contains("COPY", dockerfile);
      Assert.False(Directory.Exists(workingFolder));
    }

    [Fact]
    public async Task ToDockerfileStringAsync_WithMissingCopySource_DeletesOwnedTempFolder()
    {
      var missing = Path.GetFullPath(Path.Combine(".out", "chunk1-prod-ready", "missing-copy.txt"));
      var builder = new DockerfileBuilder()
          .UseParent("alpine")
          .Copy(missing, "/app/missing-copy.txt");
      var workingFolder = GetWorkingFolder(builder);

      var dockerfile = await builder.ToDockerfileStringAsync(TestContext.Current.CancellationToken);

      Assert.Contains("COPY", dockerfile);
      Assert.False(Directory.Exists(workingFolder));
    }

    [Fact]
    public async Task ToDockerfileStringAsync_WithUserWorkingFolder_DoesNotDeleteFolder()
    {
      var workingFolder = Path.GetFullPath(Path.Combine(".out", "chunk1-prod-ready", "user-working-folder"));
      Directory.CreateDirectory(workingFolder);
      var builder = new DockerfileBuilder()
          .WorkingFolder(workingFolder)
          .UseParent("alpine");

      await builder.ToDockerfileStringAsync(TestContext.Current.CancellationToken);

      Assert.True(Directory.Exists(workingFolder));
    }

    [Fact]
    public async Task ContainerDuplicatePortMapping_ThrowsAtBuild()
    {
      await using var kernel = (await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker")).kernel;

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver("docker", kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithPort("8080", "80")
              .WithPort("9090", "80"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("Duplicate container port mapping for '80/tcp'", ex.Message);
    }

    [Fact]
    public async Task ContainerDuplicatePortMapping_AllowsIdenticalRespec()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack
          .SetupContainerCreate("container-1")
          .SetupContainerStart()
          .SetupContainerInspect("container-1", running: true);

      await using (kernel)
      await using (await new Builder()
          .WithinDriver("docker", kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithPort("8080", "80")
              .WithPort("8080", "80"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken))
      {
      }
    }

    [Fact]
    public async Task ComposeEnvFiles_LastFileWinsQuotesAreStrippedAndExplicitEnvironmentWins()
    {
      var first = TestEnvFilePath("precedence-first");
      var second = TestEnvFilePath("precedence-second");
      await File.WriteAllTextAsync(first, "A=first\nB='quoted first'\n", TestContext.Current.CancellationToken);
      await File.WriteAllTextAsync(second, "export A=second\nB=\"quoted second\"\n", TestContext.Current.CancellationToken);
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });
      await using (kernel)
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithEnvFile(first)
                .WithEnvFile(second)
                .WithEnvironment("B", "explicit"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
      }

      mockPack.ComposeDriver.Verify(d => d.UpAsync(
          It.IsAny<DriverContext>(),
          It.Is<ComposeUpConfig>(c =>
              c.Environment["A"] == "second" &&
              c.Environment["B"] == "explicit"),
          It.IsAny<CancellationToken>()), Times.Once);
      File.Delete(first);
      File.Delete(second);
    }

    [Fact]
    public async Task UseImage_WithDigestReference_ThrowsClearError()
    {
      await using var kernel = (await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker")).kernel;

      var ex = Assert.Throws<FluentDockerException>(() => new Builder()
          .WithinDriver("docker", kernel)
          .UseImage("repo@sha256:abc", d => d.FromString("FROM scratch")));

      Assert.Contains("Digest image references are not valid build output names", ex.Message);
    }

    [Fact]
    public async Task WithWaitPollInterval_RejectsZero()
    {
      await using var kernel = (await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker")).kernel;

      var ex = Assert.Throws<FluentDockerException>(() => new Builder()
          .WithinDriver("docker", kernel)
          .UseContainer(c => c.UseImage("alpine").WithWaitPollInterval(0)));

      Assert.Contains("Wait poll interval must be at least 1 ms", ex.Message);
    }

    private static string TestEnvFilePath(string name)
    {
      var directory = Path.GetFullPath(Path.Combine(".out", "chunk1-prod-ready"));
      Directory.CreateDirectory(directory);
      return Path.Combine(directory, $"{name}.env");
    }

    private static string GetWorkingFolder(DockerfileBuilder builder)
    {
      var field = typeof(DockerfileBuilder).GetField(
          "_workingFolder", BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(field);
      return ((TemplateString)field.GetValue(builder)!).Rendered;
    }
  }
}
