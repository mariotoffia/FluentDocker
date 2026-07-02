using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Tests for the buffered (non-streaming) command timeout (FIX-1) and the stderr
  /// truncation cap (FIX-3) on <see cref="PodmanCliDriverBase"/>. A real POSIX shell is used
  /// as a stand-in podman binary, so these are skipped on Windows.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanCliBufferedTimeoutTests
  {
    private sealed class ShellDriver : PodmanCliDriverBase
    {
      public ShellDriver(IPodmanBinaryResolver resolver) : base(resolver)
      {
      }

      public Task<SimpleCommandResult> Run(string args, CancellationToken ct) =>
          ExecuteCommandAsync(args, ct);

      public Task<SimpleCommandResult> RunUnbounded(string args, CancellationToken ct) =>
          ExecuteUnboundedCommandAsync(args, ct);
    }

    private static ShellDriver CreateShellDriver(TimeSpan? requestTimeout)
    {
      var resolver = new Mock<IPodmanBinaryResolver>();
      resolver.Setup(r => r.Resolve(It.IsAny<string>()))
          .Returns(new PodmanBinary("/bin", "sh", SudoMechanism.None, null!, PodmanBinaryType.PodmanClient));
      var driver = new ShellDriver(resolver.Object);
      driver.Initialize(new DriverContext("podman") { RequestTimeout = requestTimeout });
      return driver;
    }

    [Fact]
    public async Task BufferedCommand_HangsPastRequestTimeout_ThrowsTimeoutDriverException()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellDriver(TimeSpan.FromMilliseconds(300));

      var ex = await Assert.ThrowsAsync<DriverException>(() =>
          driver.Run("-c \"sleep 30\"", CancellationToken.None));

      Assert.Equal(ErrorCodes.General.Timeout, ex.ErrorCode);
      Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BufferedCommand_CallerCancels_RethrowsOperationCanceled_NotTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellDriver(TimeSpan.FromMinutes(5));
      using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          driver.Run("-c \"sleep 30\"", cts.Token));
    }

    [Fact]
    public async Task BufferedCommand_WithinTimeout_Succeeds()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellDriver(TimeSpan.FromSeconds(30));

      var result = await driver.Run("-c \"printf done\"", TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("done", result.Output);
    }

    [Fact]
    public async Task UnboundedCommand_IgnoresRequestTimeout_WhileBoundedAborts()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      // With a tiny RequestTimeout the unbounded path must COMPLETE a 1s sleep (inherently-long
      // ops honour only caller cancellation), while the default bounded path aborts the same
      // command with a Timeout DriverException — proving the exemption.
      var driver = CreateShellDriver(TimeSpan.FromMilliseconds(200));

      var result = await driver.RunUnbounded("-c \"sleep 1; printf done\"", TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal("done", result.Output);

      var ex = await Assert.ThrowsAsync<DriverException>(() =>
          driver.Run("-c \"sleep 1\"", CancellationToken.None));
      Assert.Equal(ErrorCodes.General.Timeout, ex.ErrorCode);
    }

    [Fact]
    public async Task UnboundedCommand_StillHonoursCallerCancellation()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellDriver(requestTimeout: null);
      using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          driver.RunUnbounded("-c \"sleep 30\"", cts.Token));
    }

    [Fact]
    public async Task UnboundedCommand_LargeStdout_NotCappedAt4MiB_KeepsTail()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      // The bounded path FAILS a command whose stdout exceeds the 4 MiB cap (see
      // BufferedCommand_OversizedStdout_FailsCleanly). The unbounded path must instead stream to
      // completion, keeping a bounded rolling TAIL — so the meaningful trailing line (here a
      // marker; for real ops the loaded-image / id / exit-code line) survives even though total
      // output far exceeds 4 MiB.
      var driver = CreateShellDriver(requestTimeout: null);

      // ~5.6 MiB of stdout ("padding\n" == 8 bytes x 700000), then a trailing marker (no newline).
      const string script = "yes padding | head -n 700000; printf DONE_MARKER";
      var result = await driver.RunUnbounded($"-c \"{script}\"", TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(0, result.ExitCode);
      Assert.Contains("DONE_MARKER", result.Output);
      Assert.True(result.Output.Length < 4 * 1024 * 1024,
          "unbounded output must be kept as a bounded rolling tail, not buffered in full");
    }

    [Fact]
    public async Task BufferedCommand_OversizedStderr_TruncatedNotThrown_AndExitPreserved()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellDriver(TimeSpan.FromMinutes(5));

      // Flood stderr well past the 4 MiB cap, write a small stdout, then exit non-zero.
      // stderr must be truncated (not throw) so the error head is preserved, and the real
      // exit code must survive.
      const string script = "printf ok; yes errpadding | head -n 5000000 1>&2; exit 5";
      var result = await driver.Run($"-c \"{script}\"", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(5, result.ExitCode);
      Assert.Equal("ok", result.Output);
      Assert.Contains("errpadding", result.Error);
      Assert.Contains("truncated", result.Error, StringComparison.OrdinalIgnoreCase);
      Assert.True(result.Error.Length < 5 * 1024 * 1024,
          "stderr must be capped near the 4 MiB limit, not buffered in full");
    }

    [Fact]
    public async Task BufferedCommand_OversizedStdout_FailsCleanly_WithoutHanging()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellDriver(TimeSpan.FromMinutes(5));

      // Flood stdout far past the 4 MiB hard cap. Unlike stderr, stdout fails fast
      // (ReadBoundedAsync throws), landing in the generic catch which kills the still-flooding
      // child so the call returns promptly with the -1 sentinel instead of orphaning a process
      // blocked on a full pipe. (The kill itself is internal; the observable contract is a
      // clean, prompt failure carrying the cap diagnostic.)
      const string script = "yes outpadding | head -n 50000000";
      var result = await driver.Run($"-c \"{script}\"", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(-1, result.ExitCode);
      Assert.Contains("exceeded", result.Error, StringComparison.OrdinalIgnoreCase);
    }
  }
}
