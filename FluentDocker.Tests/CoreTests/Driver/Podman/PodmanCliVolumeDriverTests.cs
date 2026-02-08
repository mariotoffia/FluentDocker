using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Volumes;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
    /// <summary>
    /// Unit tests for PodmanCliVolumeDriver JSON parsing.
    /// </summary>
    [Trait("Category", "Unit")]
    public class PodmanCliVolumeDriverTests
    {
        [Fact]
        public void ParseVolumeList_JsonArray_ReturnsVolumes()
        {
            var json = @"[
                {""Name"":""vol1"",""Driver"":""local"",""Scope"":""local""},
                {""Name"":""vol2"",""Driver"":""nfs"",""Scope"":""global""}
            ]";

            var result = InvokeParseVolumeList(json);
            Assert.Equal(2, result.Count);
            Assert.Equal("vol1", result[0].Name);
            Assert.Equal("local", result[0].Driver);
            Assert.Equal("local", result[0].Scope);
            Assert.Equal("vol2", result[1].Name);
            Assert.Equal("nfs", result[1].Driver);
        }

        [Fact]
        public void ParseVolumeList_NewlineDelimitedJson_ReturnsVolumes()
        {
            var json = "{\"Name\":\"vol1\",\"Driver\":\"local\",\"Scope\":\"local\"}\n"
                     + "{\"Name\":\"vol2\",\"Driver\":\"local\",\"Scope\":\"local\"}";

            var result = InvokeParseVolumeList(json);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void ParseVolumeList_EmptyString_ReturnsEmpty()
        {
            var result = InvokeParseVolumeList("");
            Assert.Empty(result);
        }

        [Fact]
        public void ParseVolumeList_NullString_ReturnsEmpty()
        {
            var result = InvokeParseVolumeList(null);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseVolumeInspect_JsonArray_ReturnsFirstVolume()
        {
            var json = @"[{""Name"":""test-vol"",""Driver"":""local"",""Scope"":""local""}]";

            var result = InvokeParseVolumeInspect(json);
            Assert.Equal("test-vol", result.Name);
            Assert.Equal("local", result.Driver);
        }

        [Fact]
        public void ParseVolumeInspect_SingleJsonObject_ReturnsVolume()
        {
            var json = @"{""Name"":""test-vol"",""Driver"":""local"",""Scope"":""local""}";

            var result = InvokeParseVolumeInspect(json);
            Assert.Equal("test-vol", result.Name);
        }

        [Fact]
        public void ParseVolumeInspect_InvalidJson_ReturnsEmptyVolume()
        {
            var result = InvokeParseVolumeInspect("not json");
            Assert.NotNull(result);
        }

        #region Reflection Helpers

        private static IList<Volume> InvokeParseVolumeList(string json)
        {
            var method = typeof(PodmanCliVolumeDriver).GetMethod(
                "ParseVolumeList",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (IList<Volume>)method.Invoke(null, new object[] { json });
        }

        private static Volume InvokeParseVolumeInspect(string json)
        {
            var method = typeof(PodmanCliVolumeDriver).GetMethod(
                "ParseVolumeInspect",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (Volume)method.Invoke(null, new object[] { json });
        }

        #endregion
    }
}
