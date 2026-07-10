using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Model.Builders.FileBuilder;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public sealed class ProdReadyBuildersFixesTests : MockKernelTestBase, IAsyncLifetime
  {
    public ValueTask InitializeAsync() => new(InitializeMockKernelAsync());

    [Fact]
    public async Task DockerfileBuilder_FromFileWithFluentCommand_Throws()
    {
      Directory.CreateDirectory(".out/prod-ready-builders");
      var dockerfile = Path.Combine(".out", "prod-ready-builders", "Dockerfile");
      await File.WriteAllTextAsync(
          dockerfile, "FROM alpine\n", TestContext.Current.CancellationToken);

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new DockerfileBuilder()
          .FromFile(dockerfile)
          .Run("echo should-not-disappear")
          .ToDockerfileStringAsync(TestContext.Current.CancellationToken));

      Assert.Contains("cannot be combined", ex.Message);
    }

    [Fact]
    public void ContainerBuilder_ExposePortNull_ThrowsArgumentNullException()
    {
      Assert.Throws<ArgumentNullException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.ExposePort(null!)));
    }

    [Fact]
    public void ImageBuilder_ImageTagNullParamsArray_ThrowsArgumentNullException()
    {
      string[] tags = null!;
      var builder = new ImageBuilder(Kernel, DriverId);

      Assert.Throws<ArgumentNullException>(() => builder.ImageTag(tags));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ImageBuilder_ImageTagEmptyTag_ThrowsArgumentException(string tag)
    {
      var builder = new ImageBuilder(Kernel, DriverId);

      var ex = Assert.Throws<ArgumentException>(() => builder.ImageTag(tag));

      Assert.Equal("tag", ex.ParamName);
    }

    [Fact]
    public void ImageBuilder_BuildArgumentsNullParamsArray_ThrowsArgumentNullException()
    {
      string[] args = null!;
      var builder = new ImageBuilder(Kernel, DriverId);

      Assert.Throws<ArgumentNullException>(() => builder.BuildArguments(args));
    }

    [Fact]
    public void ImageBuilder_LabelNullParamsArray_ThrowsArgumentNullException()
    {
      string[] labels = null!;
      var builder = new ImageBuilder(Kernel, DriverId);

      Assert.Throws<ArgumentNullException>(() => builder.Label(labels));
    }

    [Fact]
    public void ContainerBuilder_TrailingColonImageReference_ThrowsClearException()
    {
      var ex = Assert.Throws<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("repo:")));

      Assert.Contains("empty tag", ex.Message);
    }

    [Fact]
    public async Task ContainerBuilder_ReuseIfExistsWithoutName_Throws()
    {
      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .ReuseIfExists())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("requires WithName", ex.Message);
    }

    [Fact]
    public void ContainerBuilder_WithColonHostPath_ThrowsArgumentException()
    {
      var ex = Assert.Throws<ArgumentException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.WithVolume("/host/with:colon", "/data")));

      Assert.Equal("hostPath", ex.ParamName);
    }

    [Fact]
    public void ContainerBuilder_WithNonDriveColonHostPath_ThrowsArgumentException()
    {
      var ex = Assert.Throws<ArgumentException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.WithVolume("5:foo", "/data")));

      Assert.Equal("hostPath", ex.ParamName);
    }

    [Fact]
    public void ContainerBuilder_WithDriveRelativeColonHostPath_ThrowsArgumentException()
    {
      var ex = Assert.Throws<ArgumentException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.WithVolume("C:relative", "/data")));

      Assert.Equal("hostPath", ex.ParamName);
    }

    [Theory]
    [InlineData(@"C:\foo")]
    [InlineData("c:/foo")]
    public void ContainerBuilder_WithWindowsAbsoluteHostPath_DoesNotThrow(string hostPath)
    {
      var error = Record.Exception(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.WithVolume(hostPath, "/data")));

      Assert.Null(error);
    }

    [Fact]
    public void ComposeBuilder_WithComposeFilesNull_ThrowsArgumentNullException()
    {
      string[] paths = null!;

      Assert.Throws<ArgumentNullException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithComposeFiles(paths)));
    }

    [Fact]
    public void ComposeBuilder_WithComposeFilesEmptyEntry_ThrowsArgumentException()
    {
      var ex = Assert.Throws<ArgumentException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithComposeFiles("compose.yml", "")));

      Assert.Equal("path", ex.ParamName);
    }

    [Fact]
    public void ArgCommand_DefaultValueWithSpace_IsQuoted()
    {
      var command = new ArgCommand("NAME", "a b");

      Assert.Equal("ARG NAME=\"a b\"", command.ToString());
    }

    [Fact]
    public async Task DockerfileBuilder_RootedAddBasenameCollision_Throws()
    {
      var root = Path.GetFullPath(Path.Combine(".out", "prod-ready-add-collision"));
      var one = Path.Combine(root, "one");
      var two = Path.Combine(root, "two");
      Directory.CreateDirectory(one);
      Directory.CreateDirectory(two);
      var first = Path.Combine(one, "settings.json");
      var second = Path.Combine(two, "settings.json");
      await File.WriteAllTextAsync(first, "one", TestContext.Current.CancellationToken);
      await File.WriteAllTextAsync(second, "two", TestContext.Current.CancellationToken);

      var ex = await Assert.ThrowsAsync<NotSupportedException>(() => new DockerfileBuilder()
          .WorkingFolder(Path.Combine(root, "context"))
          .UseParent("alpine")
          .Add(first, "/app/one.json")
          .Add(second, "/app/two.json")
          .ToDockerfileStringAsync(TestContext.Current.CancellationToken));

      Assert.Contains("COPY/ADD", ex.Message);
    }

    [Fact]
    public async Task DockerfileBuilder_SameRootedCopySource_CanTargetTwoDestinations()
    {
      var root = Path.GetFullPath(Path.Combine(".out", "prod-ready-same-copy"));
      Directory.CreateDirectory(root);
      var source = Path.Combine(root, "settings.json");
      await File.WriteAllTextAsync(source, "{}", TestContext.Current.CancellationToken);

      var dockerfile = await new DockerfileBuilder()
          .WorkingFolder(Path.Combine(root, "context"))
          .UseParent("alpine")
          .Copy(source, "/app/a/")
          .Copy(source, "/app/b/")
          .ToDockerfileStringAsync(TestContext.Current.CancellationToken);

      Assert.Equal(2, dockerfile.Split("COPY ", StringSplitOptions.None).Length - 1);
      Assert.Contains(@"COPY [""settings.json"", ""/app/a/""]", dockerfile);
      Assert.Contains(@"COPY [""settings.json"", ""/app/b/""]", dockerfile);
    }

    [Fact]
    public async Task DockerfileBuilder_SameRootedAddSource_CanTargetTwoDestinations()
    {
      var root = Path.GetFullPath(Path.Combine(".out", "prod-ready-same-add"));
      Directory.CreateDirectory(root);
      var source = Path.Combine(root, "settings.json");
      await File.WriteAllTextAsync(source, "{}", TestContext.Current.CancellationToken);

      var dockerfile = await new DockerfileBuilder()
          .WorkingFolder(Path.Combine(root, "context"))
          .UseParent("alpine")
          .Add(source, "/app/a/")
          .Add(source, "/app/b/")
          .ToDockerfileStringAsync(TestContext.Current.CancellationToken);

      Assert.Equal(2, dockerfile.Split("ADD ", StringSplitOptions.None).Length - 1);
      Assert.Contains(@"ADD [""settings.json"", ""/app/a/""]", dockerfile);
      Assert.Contains(@"ADD [""settings.json"", ""/app/b/""]", dockerfile);
    }

    [Fact]
    public async Task DockerfileBuilder_CopyUrlFailureDeletesPartialDownload()
    {
      var workingFolder = Path.Combine(".out", "prod-ready-url-failure");
      if (Directory.Exists(workingFolder))
        Directory.Delete(workingFolder, recursive: true);
      using var listener = StartHttpListener(out var url);
      var response = RespondPartialThenCloseAsync(listener);

      try
      {
        await Assert.ThrowsAnyAsync<Exception>(() => new DockerfileBuilder()
            .WorkingFolder(workingFolder)
            .UseParent("alpine")
            .Copy(url + "file.bin", "/app/file.bin")
            .ToDockerfileStringAsync(TestContext.Current.CancellationToken));
      }
      finally
      {
        listener.Close();
      }
      await response.ConfigureAwait(false);

      Assert.False(File.Exists(Path.Combine(workingFolder, "___fluentdockerdl", "file.bin")));
    }

    [Fact]
    public async Task WaitForHttpUrl_HugeContinuationDelay_ClampsUntilCancellation()
    {
      MockPack
          .SetupContainerCreate("http-delay-container")
          .SetupContainerStart()
          .SetupContainerInspect("http-delay-container", running: true)
          .SetupContainerRemove()
          .SetupContainerGetLogs("");
      using var listener = StartHttpListener(out var url);
      var response = RespondOnceAsync(listener);
      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      var continuationCalled = false;

      var build = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WaitForHttpUrl(url, timeoutMs: 60000, continuation: (_, _) =>
              {
                continuationCalled = true;
                cts.Cancel();
                return (long)int.MaxValue + 1;
              }))
          .BuildAsync(cancellationToken: cts.Token);

      try
      {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
      }
      finally
      {
        listener.Close();
      }
      Assert.True(continuationCalled);
      await response.ConfigureAwait(false);
    }

    private static HttpListener StartHttpListener(out string url)
    {
      var listener = LoopbackHttpListenerSupport.Start(out url);
      return listener;
    }

    private static async Task RespondOnceAsync(HttpListener listener)
    {
      try
      {
        var context = await listener.GetContextAsync().ConfigureAwait(false);
        context.Response.StatusCode = 503;
        context.Response.Close();
      }
      catch (ObjectDisposedException)
      {
      }
      catch (HttpListenerException)
      {
      }
    }

    private static async Task RespondPartialThenCloseAsync(HttpListener listener)
    {
      try
      {
        var context = await listener.GetContextAsync().ConfigureAwait(false);
        context.Response.ContentLength64 = 1024;
        var bytes = new byte[] { 1, 2, 3, 4 };
        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
        context.Response.Abort();
      }
      catch (ObjectDisposedException)
      {
      }
      catch (HttpListenerException)
      {
      }
    }

  }
}
