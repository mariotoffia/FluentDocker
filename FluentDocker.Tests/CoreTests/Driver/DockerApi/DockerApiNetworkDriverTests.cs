using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
    [Trait("Category", "Unit")]
    public class DockerApiNetworkDriverTests
    {
        private static DriverContext Ctx => new("docker-api-test");

        private static (DockerApiNetworkDriver driver, MockDockerApiConnection mock) CreateDriver()
        {
            var mock = new MockDockerApiConnection();
            var driver = new DockerApiNetworkDriver(mock);
            driver.Initialize(new DriverContext("docker-api-test"));
            return (driver, mock);
        }

        [Fact]
        public async Task CreateAsync_ReturnsIdAndWarning()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupPost("/networks/create", 200,
                @"{""Id"":""net123"",""Warning"":""some warning""}");

            var config = new NetworkCreateConfig { Name = "my-net" };
            var result = await driver.CreateAsync(Ctx, config);

            Assert.True(result.Success);
            Assert.Equal("net123", result.Data.Id);
            Assert.Single(result.Data.Warnings);
            Assert.Equal("some warning", result.Data.Warnings[0]);
        }

        [Fact]
        public async Task CreateAsync_EmptyWarning_ProducesNoWarnings()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupPost("/networks/create", 200,
                @"{""Id"":""net456"",""Warning"":""""}");

            var config = new NetworkCreateConfig { Name = "net2" };
            var result = await driver.CreateAsync(Ctx, config);

            Assert.True(result.Success);
            Assert.Equal("net456", result.Data.Id);
            Assert.Single(result.Data.Warnings);
        }

        [Fact]
        public async Task ListAsync_ReturnsParsedNetworks()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupGet("/networks", 200,
                @"[{""Id"":""n1"",""Name"":""bridge"",""Driver"":""bridge"",""Scope"":""local""},"
                + @"{""Id"":""n2"",""Name"":""host"",""Driver"":""host"",""Scope"":""local""}]");

            var result = await driver.ListAsync(Ctx);

            Assert.True(result.Success);
            Assert.Equal(2, result.Data.Count);
            Assert.Equal("n1", result.Data[0].Id);
            Assert.Equal("bridge", result.Data[0].Name);
            Assert.Equal("bridge", result.Data[0].Driver);
            Assert.Equal("local", result.Data[0].Scope);
            Assert.Equal("n2", result.Data[1].Id);
            Assert.Equal("host", result.Data[1].Name);
        }

        [Fact]
        public async Task ListAsync_EmptyArray_ReturnsEmptyList()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupGet("/networks", 200, "[]");

            var result = await driver.ListAsync(Ctx);

            Assert.True(result.Success);
            Assert.Empty(result.Data);
        }

        [Fact]
        public async Task InspectAsync_ReturnsParsedNetwork()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupGet("/networks/net789", 200,
                @"{""Id"":""net789"",""Name"":""custom-net"",""Driver"":""overlay"","
                + @"""Scope"":""swarm"",""Internal"":true,""EnableIPv6"":false,"
                + @"""Labels"":{""env"":""test""}}");

            var result = await driver.InspectAsync(Ctx, "net789");

            Assert.True(result.Success);
            Assert.Equal("net789", result.Data.Id);
            Assert.Equal("custom-net", result.Data.Name);
            Assert.Equal("overlay", result.Data.Driver);
            Assert.Equal("swarm", result.Data.Scope);
            Assert.True(result.Data.Internal);
            Assert.False(result.Data.IPv6);
            Assert.Equal("test", result.Data.Labels["env"]);
        }

        [Fact]
        public async Task InspectAsync_404_ReturnsNotFoundError()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupGet("/networks/missing", 404,
                @"{""message"":""network missing not found""}");

            var result = await driver.InspectAsync(Ctx, "missing");

            Assert.False(result.Success);
            Assert.Equal(ErrorCodes.Network.NotFound, result.ErrorCode);
            Assert.Contains("not found", result.Error);
        }

        [Fact]
        public async Task RemoveAsync_ReturnsSuccess()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupDelete("/networks/net-del", 204, "");

            var result = await driver.RemoveAsync(Ctx, "net-del");

            Assert.True(result.Success);

            var requests = mock.GetRequests();
            Assert.Contains(requests, r => r.Method == "DELETE" && r.Path.Contains("/networks/net-del"));
        }

        [Fact]
        public async Task RemoveAsync_404_ReturnsNotFoundError()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupDelete("/networks/gone", 404,
                @"{""message"":""network gone not found""}");

            var result = await driver.RemoveAsync(Ctx, "gone");

            Assert.False(result.Success);
            Assert.Equal(ErrorCodes.Network.NotFound, result.ErrorCode);
        }

        [Fact]
        public async Task ConnectAsync_ReturnsSuccess()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupPost("/networks/net1/connect", 200, "{}");

            var result = await driver.ConnectAsync(Ctx, "net1", "container-abc");

            Assert.True(result.Success);

            var requests = mock.GetRequests();
            var post = requests.First(r =>
                r.Method == "POST" && r.Path.Contains("/networks/net1/connect"));
            Assert.Contains("container-abc", post.Body);
        }

        [Fact]
        public async Task ConnectAsync_ServerError_ReturnsConnectFailed()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupPost("/networks/net1/connect", 500,
                @"{""message"":""internal error""}");

            var result = await driver.ConnectAsync(Ctx, "net1", "ctr1");

            Assert.False(result.Success);
            Assert.Equal(ErrorCodes.Network.ConnectFailed, result.ErrorCode);
        }

        [Fact]
        public async Task DisconnectAsync_ReturnsSuccess()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupPost("/networks/net2/disconnect", 200, "{}");

            var result = await driver.DisconnectAsync(Ctx, "net2", "container-xyz");

            Assert.True(result.Success);

            var requests = mock.GetRequests();
            var post = requests.First(r =>
                r.Method == "POST" && r.Path.Contains("/networks/net2/disconnect"));
            Assert.Contains("container-xyz", post.Body);
        }

        [Fact]
        public async Task DisconnectAsync_ServerError_ReturnsDisconnectFailed()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupPost("/networks/net2/disconnect", 403,
                @"{""message"":""forbidden""}");

            var result = await driver.DisconnectAsync(Ctx, "net2", "ctr1");

            Assert.False(result.Success);
            Assert.Equal(ErrorCodes.Network.DisconnectFailed, result.ErrorCode);
        }

        [Fact]
        public async Task PruneAsync_ReturnsDeletedNetworks()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupPost("/networks/prune", 200,
                @"{""NetworksDeleted"":[""old-net-1"",""old-net-2""]}");

            var result = await driver.PruneAsync(Ctx);

            Assert.True(result.Success);
            Assert.Equal(2, result.Data.NetworksDeleted.Count);
            Assert.Contains("old-net-1", result.Data.NetworksDeleted);
            Assert.Contains("old-net-2", result.Data.NetworksDeleted);
        }

        [Fact]
        public async Task PruneAsync_EmptyResult_ReturnsEmptyList()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupPost("/networks/prune", 200, "{}");

            var result = await driver.PruneAsync(Ctx);

            Assert.True(result.Success);
            Assert.Empty(result.Data.NetworksDeleted);
        }

        [Fact]
        public async Task PruneAsync_ServerError_ReturnsPruneFailed()
        {
            var (driver, mock) = CreateDriver();
            mock.SetupPost("/networks/prune", 500,
                @"{""message"":""prune failed""}");

            var result = await driver.PruneAsync(Ctx);

            Assert.False(result.Success);
            Assert.Equal(ErrorCodes.Network.PruneFailed, result.ErrorCode);
        }
    }
}
