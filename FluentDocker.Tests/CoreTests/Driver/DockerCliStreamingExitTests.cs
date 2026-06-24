using System;
using System.Collections.Generic;
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
  /// Tests for <see cref="DockerCliDriverBase"/> streaming command execution: stderr
  /// is drained concurrently (no deadlock on a chatty child) and a non-zero exit is
  /// surfaced as a <see cref="DriverException"/> instead of ending the stream silently.
  /// Uses a real POSIX shell, so the test is skipped on Windows.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class DockerCliStreamingExitTests
  {
    private sealed class ShellStreamDriver : DockerCliDriverBase
    {
      public ShellStreamDriver(IBinaryResolver resolver) : base(resolver)
      {
      }

      public IAsyncEnumerable<string> Stream(string args, CancellationToken ct) =>
          ExecuteStreamingCommandAsync(args, ct);
    }

    private static ShellStreamDriver CreateShellDriver()
    {
      var resolver = new Mock<IBinaryResolver>();
      resolver.Setup(r => r.Resolve(It.IsAny<string>()))
          .Returns(new DockerBinary("/bin", "sh", SudoMechanism.None, null, DockerBinaryType.DockerClient));
      var driver = new ShellStreamDriver(resolver.Object);
      driver.Initialize(new DriverContext("docker")); // no Host -> no global args prepended
      return driver;
    }

    [Fact]
    public async Task Streaming_DrainsStderr_AndThrowsOnNonZeroExit()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell/signal streaming semantics; not applicable on Windows");

      var driver = CreateShellDriver();

      // 2 stdout lines, then flood stderr well past the pipe buffer (proving concurrent
      // drain prevents a deadlock), then exit non-zero (must throw, not end silently).
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
    }

    [Fact]
    public async Task Streaming_DeliversAllEmittedLines_BeforeThrowingOnNonZeroExit()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell/signal streaming semantics; not applicable on Windows");

      var driver = CreateShellDriver();

      // Emit several stdout lines, then exit non-zero. Consumers (logs/events/stats)
      // must receive every line first, and only then observe the failure surfaced as a
      // DriverException once the stream ends.
      const string script = "printf 'one\\ntwo\\nthree\\nfour\\n'; exit 7";
      var args = $"-c \"{script}\"";

      var lines = new List<string>();
      var ex = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var line in driver.Stream(args, TestContext.Current.CancellationToken))
          lines.Add(line);
      });

      // (a) all emitted lines were delivered to the enumerator before the throw.
      Assert.Equal(new[] { "one", "two", "three", "four" }, lines);
      // (b) enumerating to completion threw the documented failure once the stream ended.
      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, ex.ErrorCode);
    }

    [Fact]
    public async Task Streaming_ZeroExit_CompletesWithoutThrowing()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell/signal streaming semantics; not applicable on Windows");

      var driver = CreateShellDriver();

      var lines = new List<string>();
      await foreach (var line in driver.Stream("-c \"printf 'a\\nb\\nc\\n'\"", TestContext.Current.CancellationToken))
        lines.Add(line);

      Assert.Equal(new[] { "a", "b", "c" }, lines);
    }
  }
}
