using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Tests for <see cref="PodmanCliDriverBase"/> streaming command execution (FIX-3): stderr
  /// is drained concurrently (no deadlock on a chatty child) and a non-zero exit is surfaced
  /// as a <see cref="DriverException"/> instead of ending the stream silently. Uses a real
  /// POSIX shell, so the test is skipped on Windows.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanCliStreamingExitTests
  {
    private sealed class ShellStreamDriver : PodmanCliDriverBase
    {
      public ShellStreamDriver(IPodmanBinaryResolver resolver) : base(resolver)
      {
      }

      public IAsyncEnumerable<string> Stream(string args, CancellationToken ct) =>
          ExecuteStreamingCommandAsync(args, ct);

      public IAsyncEnumerable<string> StreamWithProgress(string args, CancellationToken ct) =>
          ExecuteStreamingCommandWithProgressAsync(args, ct);

      public IAsyncEnumerable<FluentDocker.Drivers.LogEntry> StreamWithSources(string args, CancellationToken ct) =>
          ExecuteStreamingCommandWithSourcesAsync(new DriverContext("podman"), args, stdout: true, stderr: true, ct);
    }

    private static ShellStreamDriver CreateShellDriver()
    {
      var resolver = new Mock<IPodmanBinaryResolver>();
      resolver.Setup(r => r.Resolve(It.IsAny<string>()))
          .Returns(new PodmanBinary("/bin", "sh", SudoMechanism.None, null!, PodmanBinaryType.PodmanClient));
      var driver = new ShellStreamDriver(resolver.Object);
      driver.Initialize(new DriverContext("podman")); // no Host -> no global args prepended
      return driver;
    }

    [Fact]
    public async Task Streaming_DrainsStderr_AndThrowsOnNonZeroExit()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell/signal streaming semantics; not applicable on Windows");

      var driver = CreateShellDriver();

      // 2 stdout lines, then flood stderr well past the pipe buffer (proving concurrent drain
      // prevents a deadlock), then exit non-zero (must throw, not end silently).
      const string script = "printf 'line1\\nline2\\n'; for i in $(seq 1 5000); do echo errpadding$i 1>&2; done; exit 3";
      var args = $"-c \"{script}\"";

      var lines = new List<string>();
      var ex = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var line in driver.Stream(args, TestContext.Current.CancellationToken))
          lines.Add(line);
      });

      Assert.Equal(new[] { "line1", "line2" }, lines);
      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, ex.ErrorCode);
      Assert.Contains("errpadding", ex.Message); // stderr was captured and surfaced
      Assert.Contains("exit code 3", ex.Message);
      // This path drains stderr separately, so its ErrorContext carries pure stderr and must
      // NOT carry the "merged output" label used by the interleaving paths (POD-11).
      Assert.NotNull(ex.Context);
      Assert.StartsWith("errpadding", ex.Context!.StdErr);
    }

    [Fact]
    public async Task StreamingWithProgress_NonZeroExit_LabelsTailAsMergedOutput()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell/signal streaming semantics; not applicable on Windows");

      var driver = CreateShellDriver();
      var args = "-c \"echo out; echo err 1>&2; exit 4\"";

      var ex = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamWithProgress(args, TestContext.Current.CancellationToken))
        {
        }
      });

      // POD-11: the tail interleaves stdout and stderr, so the ErrorContext must label it as
      // merged output rather than presenting it as pure stderr.
      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, ex.ErrorCode);
      Assert.NotNull(ex.Context);
      Assert.StartsWith("merged output", ex.Context!.StdErr);
      Assert.Contains("exit code 4", ex.Message);
    }

    [Fact]
    public async Task StreamingWithSources_NonZeroExit_LabelsTailAsMergedOutput()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell/signal streaming semantics; not applicable on Windows");

      var driver = CreateShellDriver();
      var args = "-c \"echo out; echo err 1>&2; exit 5\"";

      var ex = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamWithSources(args, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, ex.ErrorCode);
      Assert.NotNull(ex.Context);
      Assert.StartsWith("merged output", ex.Context!.StdErr);
      Assert.Contains("exit code 5", ex.Message);
    }

    [Fact]
    public async Task Streaming_ZeroExit_DeliversAllLines_NoThrow()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell/signal streaming semantics; not applicable on Windows");

      var driver = CreateShellDriver();
      var args = "-c \"printf 'a\\nb\\nc\\n'\"";

      var lines = new List<string>();
      await foreach (var line in driver.Stream(args, TestContext.Current.CancellationToken))
        lines.Add(line);

      Assert.Equal(new[] { "a", "b", "c" }, lines);
    }
  }
}
