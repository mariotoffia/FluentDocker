using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Tests for ExecuteOnRunning lifecycle execution (issue #283): each command runs as a
  /// separate command, exactly once, AFTER wait conditions, and failures propagate.
  /// </summary>
  public partial class BuilderContainerTests
  {
    private void SetupLifecycleBuild() =>
        MockPack
            .SetupContainerCreate()
            .SetupContainerStart()
            .SetupContainerInspect(running: true)
            .SetupContainerStop()
            .SetupContainerRemove();

    [Fact]
    public async Task ExecuteOnRunning_RunsEachCommandAsSeparateCommand_ExactlyOnce()
    {
      SetupLifecycleBuild();

      var execCommands = new List<string>();
      MockPack.ContainerDriver
          .Setup(d => d.ExecAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(),
              It.IsAny<ExecConfig>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, ExecConfig, CancellationToken>(
              (_, _, cfg, _) => execCommands.Add(string.Join(" ", cfg.Command)))
          .ReturnsAsync(CommandResponse<ExecResult>.Ok(new ExecResult { StdOut = "", ExitCode = 0 }));

      using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .ExecuteOnRunning("first", "second"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Each array element runs as its own command (not joined) and exactly once (no double-exec).
      Assert.Equal(new[] { "first", "second" }, execCommands);
    }

    [Fact]
    public async Task ExecuteOnRunning_RunsAfterWaitConditions()
    {
      SetupLifecycleBuild();

      var order = new List<string>();
      MockPack.ContainerDriver
          .Setup(d => d.ExecAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(),
              It.IsAny<ExecConfig>(), It.IsAny<CancellationToken>()))
          .Callback(() => order.Add("exec"))
          .ReturnsAsync(CommandResponse<ExecResult>.Ok(new ExecResult { StdOut = "", ExitCode = 0 }));

      using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .Wait((_, _) => { order.Add("wait"); return -1; })
              .ExecuteOnRunning("after-ready"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(new[] { "wait", "exec" }, order);
    }

    [Fact]
    public async Task ExecuteOnRunning_FailingCommand_PropagatesError()
    {
      SetupLifecycleBuild();

      MockPack.ContainerDriver
          .Setup(d => d.ExecAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(),
              It.IsAny<ExecConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ExecResult>.Fail("exec boom", "CONTAINER_EXEC_FAILED"));

      await Assert.ThrowsAsync<DriverException>(async () =>
          await new Builder()
              .WithinDriver(DriverId, Kernel)
              .UseContainer(c => c
                  .UseImage("alpine")
                  .ExecuteOnRunning("will-fail"))
              .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
    }
  }
}
