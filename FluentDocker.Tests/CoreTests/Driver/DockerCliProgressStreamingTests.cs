using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Tests for the two distinct streaming paths on <see cref="DockerCliDriverBase"/> and
  /// the non-streaming stdout sanity cap (M1):
  /// <list type="bullet">
  /// <item><b>NEW3</b>: <c>ExecuteStreamingCommandWithProgressAsync</c> interleaves stderr
  /// (where <c>docker model pull</c> emits progress) into the yielded sequence, while the
  /// existing stdout-only <c>ExecuteStreamingCommandAsync</c> remains unchanged.</item>
  /// <item><b>M1</b>: a non-streaming command whose stdout exceeds the cap fails with a
  /// bounded <see cref="DriverException"/> instead of buffering without limit.</item>
  /// </list>
  /// Uses a real POSIX shell, so these are skipped on Windows.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class DockerCliProgressStreamingTests
  {
    private sealed class ShellDriver : DockerCliDriverBase
    {
      public ShellDriver(IBinaryResolver resolver) : base(resolver)
      {
      }

      public IAsyncEnumerable<string> StreamStdoutOnly(string args, CancellationToken ct) =>
          ExecuteStreamingCommandAsync(args, ct);

      public IAsyncEnumerable<string> StreamWithProgress(string args, CancellationToken ct) =>
          ExecuteStreamingCommandWithProgressAsync(args, ct);

      public Task<SimpleCommandResult> Run(string args, CancellationToken ct) =>
          ExecuteCommandAsync(args, ct);
    }

    private static ShellDriver CreateShellDriver()
    {
      var resolver = new Mock<IBinaryResolver>();
      resolver.Setup(r => r.Resolve(It.IsAny<string>()))
          .Returns(new DockerBinary("/bin", "sh", SudoMechanism.None, null, DockerBinaryType.DockerClient));
      var driver = new ShellDriver(resolver.Object);
      driver.Initialize(new DriverContext("docker")); // no Host -> no global args prepended
      return driver;
    }

    [Fact]
    public async Task ProgressStream_InterleavesStderrLines_WithStdout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell streaming semantics; not applicable on Windows");

      var driver = CreateShellDriver();

      // Emit a stdout line and several stderr "progress" lines, then exit 0. The
      // progress path MUST surface the stderr lines (the stdout-only path would drop them).
      const string script = "echo out1; echo err1 1>&2; echo err2 1>&2; echo out2";
      var args = $"-c \"{script}\"";

      var lines = new List<string>();
      await foreach (var line in driver.StreamWithProgress(args, TestContext.Current.CancellationToken))
        lines.Add(line);

      Assert.Contains("out1", lines);
      Assert.Contains("out2", lines);
      Assert.Contains("err1", lines); // stderr progress is interleaved (the NEW3 fix)
      Assert.Contains("err2", lines);
    }

    [Fact]
    public async Task StdoutOnlyStream_DoesNotIncludeStderr_Unchanged()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell streaming semantics; not applicable on Windows");

      var driver = CreateShellDriver();

      // Regression guard for NEW3: the existing stdout-only streaming path (logs/events)
      // must NOT fold stderr into its output.
      const string script = "echo out1; echo errX 1>&2; echo out2";
      var args = $"-c \"{script}\"";

      var lines = new List<string>();
      await foreach (var line in driver.StreamStdoutOnly(args, TestContext.Current.CancellationToken))
        lines.Add(line);

      Assert.Equal(new[] { "out1", "out2" }, lines);
      Assert.DoesNotContain("errX", lines);
    }

    [Fact]
    public async Task ProgressStream_NonZeroExit_Throws()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell streaming semantics; not applicable on Windows");

      var driver = CreateShellDriver();

      const string script = "echo progressing 1>&2; exit 4";
      var args = $"-c \"{script}\"";

      var ex = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamWithProgress(args, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, ex.ErrorCode);
    }

    [Fact]
    public async Task NonStreaming_OversizedStdout_FailsWithBoundedError()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellDriver();

      // Emit ~8 MiB to stdout (yes | head) — well past the 4 MiB cap. The bounded read
      // must abort and the command must report a clear failure (ExitCode -1), never OOM.
      // 'yes x' emits a 2-byte line ("x\n"); 5,000,000 lines ~= 10 MB.
      const string script = "yes x | head -n 5000000";
      var args = $"-c \"{script}\"";

      var result = await driver.Run(args, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(-1, result.ExitCode);
      Assert.Contains("limit", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonStreaming_NormalStdout_Succeeds_WithinCap()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellDriver();

      var result = await driver.Run("-c \"printf 'small json output'\"", TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("small json output", result.Output);
    }
  }
}
