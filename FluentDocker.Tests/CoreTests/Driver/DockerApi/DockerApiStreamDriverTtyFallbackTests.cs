using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class DockerApiStreamDriverTtyFallbackTests
  {
    [Fact]
    public async Task StreamLogsAsync_WhenInspectFailsAndBytesAreRawText_UsesRawReader()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/tty/json", 500, @"{""message"":""inspect failed""}");
      mock.SetupStream("/containers/tty/logs", "hello\nworld\n");
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(new DriverContext("docker-api-stream-fallback-test"));
      var lines = new List<string>();

      await foreach (var line in driver.StreamLogsAsync(
          new DriverContext("docker-api-stream-fallback-test"),
          "tty",
          new StreamLogsConfig { Follow = false },
          TestContext.Current.CancellationToken))
      {
        lines.Add(line);
      }

      Assert.Equal(["hello", "world"], lines);
    }
  }
}
