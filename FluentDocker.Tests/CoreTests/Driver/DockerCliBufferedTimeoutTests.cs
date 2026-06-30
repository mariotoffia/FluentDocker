using System;
using System.Collections.Generic;
using System.Diagnostics;
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
  /// Tests for the buffered (non-streaming) command timeout (FIX-1) and the stderr
  /// truncation cap (FIX-4) on <see cref="DockerCliDriverBase"/>. A real POSIX shell is
  /// used, so these are skipped on Windows.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class DockerCliBufferedTimeoutTests
  {
    private sealed class ShellDriver : DockerCliDriverBase
    {
      public ShellDriver(IBinaryResolver resolver) : base(resolver)
      {
      }

      public Task<SimpleCommandResult> Run(string args, CancellationToken ct) =>
          ExecuteCommandAsync(args, ct);

      public Task<SimpleCommandResult> RunUnbounded(string args, CancellationToken ct) =>
          ExecuteUnboundedCommandAsync(args, ct);
    }

    private static ShellDriver CreateShellDriver(TimeSpan? requestTimeout)
    {
      var resolver = new Mock<IBinaryResolver>();
      resolver.Setup(r => r.Resolve(It.IsAny<string>()))
          .Returns(new DockerBinary("/bin", "sh", SudoMechanism.None, null!, DockerBinaryType.DockerClient));
      var driver = new ShellDriver(resolver.Object);
      driver.Initialize(new DriverContext("docker") { RequestTimeout = requestTimeout });
      return driver;
    }

    [Fact]
    public async Task BufferedCommand_HangsPastRequestTimeout_ThrowsTimeoutDriverException()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      // RequestTimeout is honoured: a command that sleeps far longer must be aborted.
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

      // Generous RequestTimeout so the caller's cancellation — not the timeout — fires.
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

      // REV-1: with a tiny RequestTimeout the unbounded path must COMPLETE a 1s sleep
      // (inherently-long ops honour only caller cancellation), while the default bounded
      // path aborts the same command with a Timeout DriverException — proving the exemption.
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
    public async Task BufferedCommand_OversizedStderr_TruncatedNotThrown_AndExitPreserved()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellDriver(TimeSpan.FromMinutes(5));

      // Flood stderr well past the 4 MiB cap, write a small stdout, then exit non-zero.
      // stderr must be truncated (not throw) so the error message head is preserved, and
      // the real exit code must survive.
      const string script = "printf ok; yes errpadding | head -n 5000000 1>&2; exit 5";
      var result = await driver.Run($"-c \"{script}\"", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(5, result.ExitCode);
      Assert.Equal("ok", result.Output);
      Assert.Contains("errpadding", result.Error);
      Assert.Contains("truncated", result.Error, StringComparison.OrdinalIgnoreCase);
      // Truncated to roughly the cap (4 MiB) plus the short truncation note — never the full ~50 MiB.
      Assert.True(result.Error.Length < 5 * 1024 * 1024,
          "stderr must be capped near the 4 MiB limit, not buffered in full");
    }
  }
}
