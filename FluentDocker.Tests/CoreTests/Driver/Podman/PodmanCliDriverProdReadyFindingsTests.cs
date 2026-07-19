using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit coverage for the ported Docker CLI production-readiness fixes on the Podman CLI drivers:
  /// PODMAN-1 (attach honours <c>--no-stdin</c> and fails fast on unsupported stream changes),
  /// PODMAN-2 (ping caps its probe at 10s via the explicit-timeout execute overload), and
  /// PODMAN-5 (GetLogsAsync formats <c>--tail</c> with the invariant culture). The POSIX-shell
  /// fakes stand in for the <c>podman</c> binary, so those cases are skipped on Windows.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class PodmanCliDriverProdReadyFindingsTests
  {
    #region PODMAN-1: attach stream mapping

    [Fact]
    public async Task AttachAsync_StdinFalse_EmitsNoStdinFlag()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake podman; not applicable on Windows");

      var record = Path.Combine(ScratchDirectory(), $"attach-args-{Guid.NewGuid():N}.txt");
      var driver = new PodmanCliStreamDriver(CreateResolver(CreateFakePodman($$"""
#!/bin/sh
printf '%s\n' "$@" > "{{record}}"
sleep 2
""")));
      driver.Initialize(new DriverContext("podman"));

      await using var attach = (await driver.AttachAsync(
          new DriverContext("podman"),
          "ctr",
          new AttachConfig { Stdin = false },
          TestContext.Current.CancellationToken)).Data;

      await FakeProcessMarker.WaitForFileAsync(record, TestContext.Current.CancellationToken);
      var args = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      Assert.Contains("--no-stdin", args);
    }

    [Fact]
    public async Task AttachAsync_UnsupportedStreamChanges_FailFastWithInvalidArgument()
    {
      // The fail-fast returns before any process spawns, so this holds on every OS.
      var driver = new PodmanCliStreamDriver(CreateResolver("/nonexistent/podman"));
      driver.Initialize(new DriverContext("podman"));

      AttachConfig[] unsupported =
      [
        new AttachConfig { Tty = true },
        new AttachConfig { Stdout = false },
        new AttachConfig { Stderr = false },
        new AttachConfig { NoStdout = true },
        new AttachConfig { NoStderr = true }
      ];

      foreach (var config in unsupported)
      {
        var result = await driver.AttachAsync(
            new DriverContext("podman"), "ctr", config, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.General.InvalidArgument, result.ErrorCode);
      }
    }

    #endregion

    #region PODMAN-2: ping probe timeout cap

    [Fact]
    public async Task ExecuteCommandAsync_ExplicitTimeout_CapsRegardlessOfRequestTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      // A generous component RequestTimeout must NOT relax the explicit per-call timeout: the
      // 300ms ceiling aborts the 30s sleep. This is the mechanism PingAsync relies on.
      var driver = CreateShellDriver(TimeSpan.FromMinutes(5));

      var ex = await Assert.ThrowsAsync<DriverException>(() =>
          driver.RunWithTimeout("-c \"sleep 30\"", TimeSpan.FromMilliseconds(300), CancellationToken.None));

      Assert.Equal(ErrorCodes.General.Timeout, ex.ErrorCode);
    }

    [Fact]
    public async Task PingAsync_WedgedMachine_FailsWithinProbeCeiling_NotBufferedDefault()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake podman; not applicable on Windows");

      // The fake ignores its args and hangs. With RequestTimeout unset the OLD ping used the
      // 5-min buffered default (and would have observed the fake's own exit at 30s -> success);
      // the 10s cap now fails it fast. Assert well under the 5-min default to prove the cap.
      var driver = new PodmanCliSystemDriver(CreateResolver(CreateFakePodman("""
#!/bin/sh
sleep 30
""")));
      driver.Initialize(new DriverContext("podman"));

      var sw = Stopwatch.StartNew();
      var result = await driver.PingAsync(new DriverContext("podman"), TestContext.Current.CancellationToken);
      sw.Stop();

      Assert.False(result.Success);
      Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"Ping took {sw.Elapsed}; probe should cap at ~10s.");
    }

    [Fact]
    public async Task PingAsync_SmallerRequestTimeout_HonouredBelowProbeCeiling()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake podman; not applicable on Windows");

      var driver = new PodmanCliSystemDriver(CreateResolver(CreateFakePodman("""
#!/bin/sh
sleep 30
""")));
      driver.Initialize(new DriverContext("podman"));

      var sw = Stopwatch.StartNew();
      var result = await driver.PingAsync(
          new DriverContext("podman") { RequestTimeout = TimeSpan.FromMilliseconds(500) },
          TestContext.Current.CancellationToken);
      sw.Stop();

      Assert.False(result.Success);
      Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Ping took {sw.Elapsed}; a 500ms RequestTimeout must win over the 10s ceiling.");
    }

    #endregion

    #region PODMAN-5: GetLogsAsync invariant tail

    [Fact]
    public async Task GetLogsAsync_NegativeTail_UnderExoticCulture_EmitsInvariantSign()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake podman; not applicable on Windows");

      var record = Path.Combine(ScratchDirectory(), $"logs-args-{Guid.NewGuid():N}.txt");
      var driver = new PodmanCliContainerDriver(CreateResolver(CreateFakePodman($$"""
#!/bin/sh
printf '%s\n' "$@" > "{{record}}"
exit 0
""")));
      driver.Initialize(new DriverContext("podman"));

      // Only the negative sign diverges across cultures for integers; "~" reveals a leak.
      var exotic = (CultureInfo)CultureInfo.InvariantCulture.Clone();
      exotic.NumberFormat.NegativeSign = "~";
      var original = CultureInfo.CurrentCulture;
      CultureInfo.CurrentCulture = exotic;
      try
      {
        var result = await driver.GetLogsAsync(
            new DriverContext("podman"), "ctr", follow: false, tail: -1, timestamps: false,
            TestContext.Current.CancellationToken);
        Assert.True(result.Success);
      }
      finally
      {
        CultureInfo.CurrentCulture = original;
      }

      await FakeProcessMarker.WaitForFileAsync(record, TestContext.Current.CancellationToken);
      var args = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      Assert.Contains("--tail", args);
      Assert.Contains("-1", args);
      Assert.DoesNotContain("~1", args);
    }

    #endregion

    #region Helpers

    private sealed class ShellDriver(IPodmanBinaryResolver resolver) : PodmanCliDriverBase(resolver)
    {
      public Task<SimpleCommandResult> RunWithTimeout(string args, TimeSpan timeout, CancellationToken ct) =>
          ExecuteCommandAsync(args, timeout, ct);
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

    private static IPodmanBinaryResolver CreateResolver(string binaryPath)
    {
      var resolver = new Mock<IPodmanBinaryResolver>();
      resolver.Setup(r => r.Resolve(It.IsAny<string>()))
          .Returns(new PodmanBinary(
              Path.GetDirectoryName(binaryPath),
              Path.GetFileName(binaryPath),
              SudoMechanism.None,
              null!,
              PodmanBinaryType.PodmanClient));
      return resolver.Object;
    }

    private static string CreateFakePodman(string script)
    {
      var path = Path.Combine(ScratchDirectory(), $"fake-podman-{Guid.NewGuid():N}");
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      return path;
    }

    private static string ScratchDirectory()
    {
      var directory = Path.Combine(Directory.GetCurrentDirectory(), ".out", "podman-cli-prodready-tests");
      Directory.CreateDirectory(directory);
      return directory;
    }

    #endregion
  }
}
