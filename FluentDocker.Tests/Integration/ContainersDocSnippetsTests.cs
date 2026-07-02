using System;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.Integration
{
  /// <summary>
  /// Executable versions of the lifecycle snippets documented in <c>docs/containers.md</c>.
  /// Each test runs the exact documented behavior against a real Docker engine so the docs
  /// (command argv quoting, single-file copy-from, and export extraction) cannot silently
  /// drift from the implementation. Small deterministic images (alpine) and clean teardown.
  /// </summary>
  [Trait("Category", "Integration")]
  public class ContainersDocSnippetsTests
  {
    private const string Image = "alpine:latest";

    private static async Task<FluentDockerKernel> CreateDockerKernelAsync(CancellationToken cancellationToken)
    {
      var kernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerCli("docker", d => d.AsDefault())
          .BuildAsync(cancellationToken: cancellationToken);

      // Ensure the (tiny) image is present so the build is deterministic.
      var imageDriver = kernel.SysCtl<IImageDriver>("docker");
      await imageDriver.PullAsync(new DriverContext("docker"), "alpine", "latest",
          cancellationToken: cancellationToken);

      return kernel;
    }

    private static void TryDeleteDirectory(string dir)
    {
      try
      {
        if (Directory.Exists(dir))
          Directory.Delete(dir, recursive: true);
      }
      catch
      {
        // Best-effort cleanup of scratch output under .out/.
      }
    }

    /// <summary>
    /// docs/containers.md — "On Running (Lifecycle Hook)": each ExecuteOnRunning argument is a
    /// distinct argv token, so a multi-word final argument stays a single argument passed to
    /// <c>sh -c</c> instead of being re-split on whitespace.
    /// </summary>
    [Fact]
    public async Task ExecuteOnRunning_PreservesArgvTokens()
    {
      var cancellationToken = TestContext.Current.CancellationToken;
      var kernel = await CreateDockerKernelAsync(cancellationToken);

      try
      {
        var results = await new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c
                .UseImage(Image)
                .WithName($"doc-argv-{Guid.NewGuid():N}")
                .WithCommand("tail", "-f", "/dev/null")
                .ExecuteOnRunning("sh", "-c", "echo 'argv token kept' > /tmp/argv.txt"))
            .BuildAsync(cancellationToken: cancellationToken);

        try
        {
          // If the final argument had been split on whitespace, the redirect would not have
          // produced the file with this exact content.
          var container = results.Containers[0];
          var output = await container.ExecuteAsync("cat /tmp/argv.txt", cancellationToken);
          Assert.Contains("argv token kept", output);
        }
        finally
        {
          await results.DisposeAllAsync();
        }
      }
      finally
      {
        kernel.Dispose();
      }
    }

    /// <summary>
    /// docs/containers.md — "Copy from Container on Dispose": a single-file source copies to a
    /// FILE at the destination path (not a directory named after the target).
    /// </summary>
    [Fact]
    public async Task CopyFromOnDispose_SingleFile_WritesToFilePath()
    {
      var cancellationToken = TestContext.Current.CancellationToken;
      var outDir = Path.Combine(".out", "containers-doc-snippets", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(outDir);
      var hostFile = Path.Combine(outDir, "report.txt");

      var kernel = await CreateDockerKernelAsync(cancellationToken);

      try
      {
        var results = await new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c
                .UseImage(Image)
                .WithName($"doc-copy-{Guid.NewGuid():N}")
                .WithCommand("sh", "-c", "echo doc-copy-content > /tmp/report.txt && tail -f /dev/null")
                .CopyFromOnDispose("/tmp/report.txt", hostFile))
            .BuildAsync(cancellationToken: cancellationToken);

        // The CopyFrom hook fires on the Removing lifecycle — during dispose, before removal.
        await results.DisposeAllAsync();

        Assert.True(File.Exists(hostFile), "single-file copy-from should write a FILE at the destination path");
        Assert.False(Directory.Exists(hostFile), "destination must be a file, not a directory");
        Assert.Contains("doc-copy-content", await File.ReadAllTextAsync(hostFile, cancellationToken));
      }
      finally
      {
        kernel.Dispose();
        TryDeleteDirectory(outDir);
      }
    }

    /// <summary>
    /// docs/containers.md — "Export on Dispose": by default the container filesystem is written
    /// as a tar archive to the exact destination path, and the result is a readable tar.
    /// </summary>
    [Fact]
    public async Task ExportOnDispose_WritesReadableTarToFilePath()
    {
      var cancellationToken = TestContext.Current.CancellationToken;
      var outDir = Path.Combine(".out", "containers-doc-snippets", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(outDir);
      var tarPath = Path.Combine(outDir, "container.tar");

      var kernel = await CreateDockerKernelAsync(cancellationToken);

      try
      {
        var results = await new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c
                .UseImage(Image)
                .WithName($"doc-export-{Guid.NewGuid():N}")
                .WithCommand("tail", "-f", "/dev/null")
                .ExportOnDispose(tarPath))
            .BuildAsync(cancellationToken: cancellationToken);

        // The Export hook fires on the Removing lifecycle — during dispose, before removal.
        await results.DisposeAllAsync();

        Assert.True(File.Exists(tarPath), "export should write a tar file to the destination path");
        Assert.True(new FileInfo(tarPath).Length > 0, "exported tar should be non-empty");

        // A valid tar yields at least one entry (the exported rootfs).
        using var tarStream = File.OpenRead(tarPath);
        using var reader = new TarReader(tarStream);
        Assert.NotNull(reader.GetNextEntry());
      }
      finally
      {
        kernel.Dispose();
        TryDeleteDirectory(outDir);
      }
    }
  }
}
