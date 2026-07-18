using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// DC-8: <c>compose port</c> must not emit an empty <c>--protocol ""</c> when
  /// <see cref="ComposePortConfig.Protocol"/> is explicitly null/empty, and must still emit
  /// the flag when a protocol is set. Uses the recording fake-docker harness.
  /// </summary>
  public sealed partial class DockerCliProductionReadinessTests
  {
    [Fact]
    public async Task ComposePort_NullProtocol_OmitsProtocolFlag()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"compose-port-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliComposeDriver(new FakeResolver(CreateRecordingDocker(record, "0.0.0.0:8080")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.PortAsync(new DriverContext("docker"), new ComposePortConfig
      {
        ComposeFiles = ["compose.yml"],
        Service = "web",
        PrivatePort = 80,
        Protocol = null
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("0.0.0.0:8080", result.Data);
      var args = await ReadArgsAsync(record);
      Assert.DoesNotContain("--protocol", args);
      Assert.Equal(["compose", "-f", "compose.yml", "port", "web", "80"], args);
    }

    [Fact]
    public async Task ComposePort_ExplicitProtocol_EmitsProtocolFlag()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"compose-port-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliComposeDriver(new FakeResolver(CreateRecordingDocker(record, "0.0.0.0:5353")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.PortAsync(new DriverContext("docker"), new ComposePortConfig
      {
        ComposeFiles = ["compose.yml"],
        Service = "dns",
        PrivatePort = 53,
        Protocol = "udp"
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var args = await ReadArgsAsync(record);
      Assert.Equal(["compose", "-f", "compose.yml", "port", "--protocol", "udp", "dns", "53"], args);
    }
  }
}
