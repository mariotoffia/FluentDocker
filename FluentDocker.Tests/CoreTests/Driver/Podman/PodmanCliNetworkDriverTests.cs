using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
    /// <summary>
    /// Unit tests for PodmanCliNetworkDriver JSON parsing.
    /// </summary>
    [Trait("Category", "Unit")]
    public class PodmanCliNetworkDriverTests
    {
        [Fact]
        public void ParseNetworkList_JsonArray_ReturnsNetworks()
        {
            var json = @"[
                {""Id"":""net1"",""Name"":""bridge"",""Driver"":""bridge"",""Scope"":""local""},
                {""Id"":""net2"",""Name"":""mynet"",""Driver"":""macvlan"",""Scope"":""local""}
            ]";

            var result = InvokeParseNetworkList(json);
            Assert.Equal(2, result.Count);
            Assert.Equal("net1", result[0].Id);
            Assert.Equal("bridge", result[0].Name);
            Assert.Equal("bridge", result[0].Driver);
            Assert.Equal("net2", result[1].Id);
        }

        [Fact]
        public void ParseNetworkList_NewlineDelimitedJson_ReturnsNetworks()
        {
            var json = "{\"Id\":\"net1\",\"Name\":\"bridge\",\"Driver\":\"bridge\"}\n"
                     + "{\"Id\":\"net2\",\"Name\":\"mynet\",\"Driver\":\"macvlan\"}";

            var result = InvokeParseNetworkList(json);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void ParseNetworkList_AlternateKeys_HandlesIDVariant()
        {
            var json = @"[{""ID"":""net1"",""Name"":""bridge"",""Driver"":""bridge""}]";

            var result = InvokeParseNetworkList(json);
            Assert.Single(result);
            Assert.Equal("net1", result[0].Id);
        }

        [Fact]
        public void ParseNetworkList_IPv6Enabled_ParsesCorrectly()
        {
            var json = @"[{""Id"":""net1"",""Name"":""v6net"",""Driver"":""bridge"",""IPv6Enabled"":true}]";

            var result = InvokeParseNetworkList(json);
            Assert.Single(result);
            Assert.True(result[0].IPv6);
        }

        [Fact]
        public void ParseNetworkList_EmptyString_ReturnsEmpty()
        {
            var result = InvokeParseNetworkList("");
            Assert.Empty(result);
        }

        [Fact]
        public void ParseNetworkList_NullString_ReturnsEmpty()
        {
            var result = InvokeParseNetworkList(null);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseNetworkInspect_JsonArray_ReturnsFirst()
        {
            var json = @"[{""Id"":""net1"",""Name"":""bridge"",""Driver"":""bridge"",""Scope"":""local""}]";

            var result = InvokeParseNetworkInspect(json);
            Assert.Equal("net1", result.Id);
            Assert.Equal("bridge", result.Name);
            Assert.Equal("bridge", result.Driver);
            Assert.Equal("local", result.Scope);
        }

        [Fact]
        public void ParseNetworkInspect_SingleObject_ReturnsNetwork()
        {
            var json = @"{""Id"":""net1"",""Name"":""mynet"",""Driver"":""macvlan""}";

            var result = InvokeParseNetworkInspect(json);
            Assert.Equal("net1", result.Id);
            Assert.Equal("mynet", result.Name);
        }

        [Fact]
        public void ParseNetworkInspect_InvalidJson_ReturnsEmptyNetwork()
        {
            var result = InvokeParseNetworkInspect("not json");
            Assert.NotNull(result);
        }

        [Fact]
        public void ParseNetworkInspect_InternalNetwork_ParsesCorrectly()
        {
            var json = @"{""Id"":""net1"",""Name"":""internal"",""Internal"":true}";

            var result = InvokeParseNetworkInspect(json);
            Assert.True(result.Internal);
        }

        #region Reflection Helpers

        private static IList<Network> InvokeParseNetworkList(string json)
        {
            var method = typeof(PodmanCliNetworkDriver).GetMethod(
                "ParseNetworkList",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (IList<Network>)method.Invoke(null, new object[] { json });
        }

        private static Network InvokeParseNetworkInspect(string json)
        {
            var method = typeof(PodmanCliNetworkDriver).GetMethod(
                "ParseNetworkInspect",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (Network)method.Invoke(null, new object[] { json });
        }

        #endregion
    }
}
