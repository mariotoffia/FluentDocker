using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class DockerApiNetworkDriverRemediationTests
  {
    [Fact]
    public async Task CreateAsync_WithIpRange_WritesIpamIpRange()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/networks/create", 201, @"{""Id"":""net1""}");
      var driver = new DockerApiNetworkDriver(mock);
      var context = new DriverContext("docker-api");
      driver.Initialize(context);

      var result = await driver.CreateAsync(context, new NetworkCreateConfig
      {
        Name = "net",
        Subnet = "172.20.0.0/16",
        IpRange = "172.20.10.0/24"
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var body = mock.GetRequests().Single(r => r.Method == "POST").Body;
      Assert.Contains(@"""IPRange"":""172.20.10.0/24""", body);
    }
  }
}
