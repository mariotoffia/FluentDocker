using System.Reflection;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Pins the Docker/Podman status-string to <see cref="ServiceRunningState"/> mapping.
  /// <see cref="ContainerService.ParseState"/> is internal; exercised via reflection per the
  /// documented parser-testing exception (a live daemon would be needed to reach it publicly).
  /// </summary>
  [Trait("Category", "Unit")]
  public class ContainerServiceParseStateTests
  {
    private static ServiceRunningState ParseState(string state)
    {
      var method = typeof(ContainerService).GetMethod(
          "ParseState", BindingFlags.NonPublic | BindingFlags.Static);
      return (ServiceRunningState)method!.Invoke(null, [state])!;
    }

    [Theory]
    [InlineData("created", ServiceRunningState.Created)]
    [InlineData("restarting", ServiceRunningState.Starting)]
    [InlineData("running", ServiceRunningState.Running)]
    [InlineData("paused", ServiceRunningState.Paused)]
    [InlineData("exited", ServiceRunningState.Stopped)]
    [InlineData("dead", ServiceRunningState.Stopped)]
    [InlineData("removing", ServiceRunningState.Removing)]
    [InlineData("bogus", ServiceRunningState.Unknown)]
    public void ParseState_MapsDockerStatuses(string status, ServiceRunningState expected)
    {
      Assert.Equal(expected, ParseState(status));
    }

    [Fact]
    public void ParseState_Created_IsDistinctFromStarting()
    {
      // Regression (Chunk 1 M-1): mapping "created" -> Starting made a container seeded from
      // the docker "created" status a no-op on the subsequent StartAsync (oldState == newState),
      // so AddHook(Starting, ...) hooks and the Starting state-change event never fired.
      Assert.Equal(ServiceRunningState.Created, ParseState("created"));
      Assert.NotEqual(ServiceRunningState.Starting, ParseState("created"));
    }
  }
}
