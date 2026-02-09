using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using Newtonsoft.Json.Linq;
using Xunit;
using Container = FluentDocker.Model.Containers.Container;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
    /// <summary>
    /// Unit tests for PodmanCliContainerDriver parsing and argument building.
    /// </summary>
    [Trait("Category", "Unit")]
    public class PodmanCliContainerDriverTests
    {
        #region ParseContainerList Tests

        [Fact]
        public void ParseContainerList_JsonArray_ReturnsContainers()
        {
            var json = @"[
                {""Id"":""abc123"",""Image"":""nginx:latest"",""Names"":""web1"",""State"":""running""},
                {""Id"":""def456"",""Image"":""redis:7"",""Names"":""cache"",""State"":""exited""}
            ]";

            var result = InvokeParseContainerList(json);
            Assert.Equal(2, result.Count);
            Assert.Equal("abc123", result[0].Id);
            Assert.Equal("nginx:latest", result[0].Image);
            Assert.Equal("web1", result[0].Name);
            Assert.Equal("running", result[0].State.Status);
            Assert.Equal("def456", result[1].Id);
        }

        [Fact]
        public void ParseContainerList_NewlineDelimitedJson_ReturnsContainers()
        {
            var json = "{\"Id\":\"abc123\",\"Image\":\"nginx\",\"Names\":\"web1\",\"State\":\"running\"}\n"
                     + "{\"Id\":\"def456\",\"Image\":\"redis\",\"Names\":\"cache\",\"State\":\"exited\"}";

            var result = InvokeParseContainerList(json);
            Assert.Equal(2, result.Count);
            Assert.Equal("abc123", result[0].Id);
            Assert.Equal("def456", result[1].Id);
        }

        [Fact]
        public void ParseContainerList_AlternateKeys_Handles_ID_And_Name()
        {
            var json = @"[{""ID"":""abc123"",""Image"":""nginx"",""Name"":""web1"",""Status"":""running""}]";

            var result = InvokeParseContainerList(json);
            Assert.Single(result);
            Assert.Equal("abc123", result[0].Id);
            Assert.Equal("web1", result[0].Name);
            Assert.Equal("running", result[0].State.Status);
        }

        [Fact]
        public void ParseContainerList_EmptyString_ReturnsEmptyList()
        {
            var result = InvokeParseContainerList("");
            Assert.Empty(result);
        }

        [Fact]
        public void ParseContainerList_NullString_ReturnsEmptyList()
        {
            var result = InvokeParseContainerList(null);
            Assert.Empty(result);
        }

        #endregion

        #region ParseContainerInspect Tests

        [Fact]
        public void ParseContainerInspect_ValidJson_ReturnsContainer()
        {
            var json = @"[{
                ""Id"": ""abc123def456"",
                ""Image"": ""sha256:abc123"",
                ""Name"": ""test-container"",
                ""Driver"": ""overlay"",
                ""State"": {
                    ""Status"": ""running"",
                    ""Running"": true,
                    ""Paused"": false,
                    ""Dead"": false,
                    ""ExitCode"": 0
                }
            }]";

            var result = InvokeParseContainerInspect(json);
            Assert.Equal("abc123def456", result.Id);
            Assert.Equal("sha256:abc123", result.Image);
            Assert.Equal("test-container", result.Name);
            Assert.Equal("overlay", result.Driver);
            Assert.Equal("running", result.State.Status);
            Assert.True(result.State.Running);
            Assert.False(result.State.Paused);
            Assert.False(result.State.Dead);
            Assert.Equal(0, result.State.ExitCode);
        }

        [Fact]
        public void ParseContainerInspect_NonArrayJson_ReturnsContainer()
        {
            var json = @"{
                ""Id"": ""abc123"",
                ""Image"": ""nginx"",
                ""Name"": ""web"",
                ""State"": { ""Status"": ""exited"", ""Running"": false, ""ExitCode"": 1 }
            }";

            var result = InvokeParseContainerInspect(json);
            Assert.Equal("abc123", result.Id);
            Assert.False(result.State.Running);
            Assert.Equal(1, result.State.ExitCode);
        }

        [Fact]
        public void ParseContainerInspect_InvalidJson_ThrowsException()
        {
            Assert.ThrowsAny<Exception>(() => InvokeParseContainerInspect("not json"));
        }

        #endregion

        #region ParseContainerState Tests

        [Fact]
        public void ParseContainerState_NullToken_ReturnsEmptyState()
        {
            var result = InvokeParseContainerState(null);
            Assert.NotNull(result);
        }

        [Fact]
        public void ParseContainerState_ValidToken_ParsesAllFields()
        {
            var token = JObject.Parse(@"{
                ""Status"": ""paused"",
                ""Running"": false,
                ""Paused"": true,
                ""Dead"": false,
                ""ExitCode"": 0
            }");

            var result = InvokeParseContainerState(token);
            Assert.Equal("paused", result.Status);
            Assert.False(result.Running);
            Assert.True(result.Paused);
            Assert.False(result.Dead);
            Assert.Equal(0, result.ExitCode);
        }

        #endregion

        #region BuildCreateArgs Tests

        [Fact]
        public void BuildCreateArgs_MinimalConfig_ReturnsCommandAndImage()
        {
            var config = new ContainerCreateConfig { Image = "nginx:latest" };
            var result = InvokeBuildCreateArgs("create", config);

            Assert.StartsWith("create", result);
            Assert.EndsWith("nginx:latest", result);
        }

        [Fact]
        public void BuildCreateArgs_WithName_IncludesNameFlag()
        {
            var config = new ContainerCreateConfig
            {
                Image = "nginx",
                Name = "web-server"
            };
            var result = InvokeBuildCreateArgs("create", config);
            Assert.Contains("--name web-server", result);
        }

        [Fact]
        public void BuildCreateArgs_WithEnvironment_IncludesEnvFlags()
        {
            var config = new ContainerCreateConfig
            {
                Image = "nginx",
                Environment = new Dictionary<string, string>
                {
                    { "FOO", "bar" },
                    { "BAZ", "qux" }
                }
            };
            var result = InvokeBuildCreateArgs("run", config);
            Assert.Contains("-e FOO=bar", result);
            Assert.Contains("-e BAZ=qux", result);
        }

        [Fact]
        public void BuildCreateArgs_WithPortBindings_IncludesPortFlags()
        {
            var config = new ContainerCreateConfig
            {
                Image = "nginx",
                PortBindings = new Dictionary<string, string>
                {
                    { "80/tcp", "8080" }
                }
            };
            var result = InvokeBuildCreateArgs("create", config);
            Assert.Contains("-p 8080:80/tcp", result);
        }

        [Fact]
        public void BuildCreateArgs_WithVolumes_IncludesVolumeFlags()
        {
            var config = new ContainerCreateConfig
            {
                Image = "nginx",
                Volumes = new Dictionary<string, string>
                {
                    { "/data", "/host/data" }
                }
            };
            var result = InvokeBuildCreateArgs("create", config);
            Assert.Contains("-v /data:/host/data", result);
        }

        [Fact]
        public void BuildCreateArgs_WithPrivileged_IncludesFlag()
        {
            var config = new ContainerCreateConfig
            {
                Image = "nginx",
                Privileged = true
            };
            var result = InvokeBuildCreateArgs("create", config);
            Assert.Contains("--privileged", result);
        }

        [Fact]
        public void BuildCreateArgs_WithAutoRemove_IncludesRmFlag()
        {
            var config = new ContainerCreateConfig
            {
                Image = "nginx",
                AutoRemove = true
            };
            var result = InvokeBuildCreateArgs("run", config);
            Assert.Contains("--rm", result);
        }

        [Fact]
        public void BuildCreateArgs_WithHealthCheck_IncludesHealthFlags()
        {
            var config = new ContainerCreateConfig
            {
                Image = "nginx",
                HealthCheck = new HealthCheckConfig
                {
                    Test = new[] { "CMD", "curl", "-f", "http://localhost/" },
                    Interval = "30s",
                    Timeout = "10s",
                    Retries = 3,
                    StartPeriod = "5s"
                }
            };
            var result = InvokeBuildCreateArgs("create", config);
            Assert.Contains("--health-cmd", result);
            Assert.Contains("--health-interval 30s", result);
            Assert.Contains("--health-timeout 10s", result);
            Assert.Contains("--health-retries 3", result);
            Assert.Contains("--health-start-period 5s", result);
        }

        [Fact]
        public void BuildCreateArgs_WithCommand_AppendsAfterImage()
        {
            var config = new ContainerCreateConfig
            {
                Image = "ubuntu",
                Command = new[] { "bash", "-c", "echo hello" }
            };
            var result = InvokeBuildCreateArgs("run", config);
            Assert.EndsWith("ubuntu bash -c echo hello", result);
        }

        #endregion

        #region Operations Parsing Tests

        [Fact]
        public void ParseTopOutput_ValidOutput_ParsesHeadersAndProcesses()
        {
            var output = "USER\tPID\tCOMMAND\nroot\t1\tnginx\nroot\t2\tworker";
            var result = InvokeParseTopOutput(output);

            Assert.Equal(3, result.Titles.Count);
            Assert.Contains("USER", result.Titles);
            Assert.Contains("PID", result.Titles);
            Assert.Equal(2, result.Processes.Count);
        }

        [Fact]
        public void ParseTopOutput_EmptyOutput_ReturnsEmptyResult()
        {
            var result = InvokeParseTopOutput("");
            Assert.Empty(result.Titles);
            Assert.Empty(result.Processes);
        }

        [Fact]
        public void ParseDiffOutput_ValidOutput_ParsesChanges()
        {
            var output = "A /added/file\nC /changed/file\nD /deleted/file";
            var result = InvokeParseDiffOutput(output);

            Assert.Equal(3, result.Count);
            Assert.Equal("A", result[0].Kind);
            Assert.Equal("/added/file", result[0].Path);
            Assert.Equal("C", result[1].Kind);
            Assert.Equal("D", result[2].Kind);
        }

        [Fact]
        public void ParseDiffOutput_EmptyOutput_ReturnsEmpty()
        {
            var result = InvokeParseDiffOutput("");
            Assert.Empty(result);
        }

        [Fact]
        public void ParseStatsOutput_JsonObject_ParsesContainerIdAndName()
        {
            var json = @"{""ContainerID"":""abc123"",""Name"":""test""}";
            var result = InvokeParseStatsOutput(json);

            Assert.Equal("abc123", result.ContainerId);
            Assert.Equal("test", result.Name);
        }

        [Fact]
        public void ParseStatsOutput_JsonArray_ParsesFirst()
        {
            var json = @"[{""ContainerID"":""abc123"",""Name"":""test""}]";
            var result = InvokeParseStatsOutput(json);

            Assert.Equal("abc123", result.ContainerId);
            Assert.Equal("test", result.Name);
        }

        [Fact]
        public void ParseStatsOutput_AlternateKeys_ParsesContainerId()
        {
            var json = @"{""container_id"":""def456"",""name"":""web""}";
            var result = InvokeParseStatsOutput(json);

            Assert.Equal("def456", result.ContainerId);
            Assert.Equal("web", result.Name);
        }

        [Fact]
        public void ParseStatsOutput_EmptyString_ReturnsEmpty()
        {
            var result = InvokeParseStatsOutput("");
            Assert.NotNull(result);
        }

        [Fact]
        public void QuoteArgumentIfNeeded_NoSpaces_ReturnsUnquoted()
        {
            var result = InvokeQuoteArgumentIfNeeded("hello");
            Assert.Equal("hello", result);
        }

        [Fact]
        public void QuoteArgumentIfNeeded_WithSpaces_ReturnsQuoted()
        {
            var result = InvokeQuoteArgumentIfNeeded("hello world");
            Assert.Equal("\"hello world\"", result);
        }

        [Fact]
        public void QuoteArgumentIfNeeded_Empty_ReturnsEmpty()
        {
            var result = InvokeQuoteArgumentIfNeeded("");
            Assert.Equal("", result);
        }

        #endregion

        #region Reflection Helpers

        private static IList<Container> InvokeParseContainerList(string json)
        {
            return PodmanCliContainerDriver.ParseContainerList(json);
        }

        private static Container InvokeParseContainerInspect(string json)
        {
            return PodmanCliContainerDriver.ParseContainerInspect(json);
        }

        private static FluentDocker.Model.Containers.ContainerState InvokeParseContainerState(
            JToken token)
        {
            return PodmanCliContainerDriver.ParseContainerState(token);
        }

        private static string InvokeBuildCreateArgs(string command, ContainerCreateConfig config)
        {
            var method = typeof(PodmanCliContainerDriver).GetMethod(
                "BuildCreateArgs",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (string)method.Invoke(null, new object[] { command, config });
        }

        private static ContainerProcesses InvokeParseTopOutput(string output)
        {
            var method = typeof(PodmanCliContainerDriver).GetMethod(
                "ParseTopOutput",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (ContainerProcesses)method.Invoke(null, new object[] { output });
        }

        private static IList<FilesystemChange> InvokeParseDiffOutput(string output)
        {
            var method = typeof(PodmanCliContainerDriver).GetMethod(
                "ParseDiffOutput",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (IList<FilesystemChange>)method.Invoke(null, new object[] { output });
        }

        private static ContainerStatsResult InvokeParseStatsOutput(string json)
        {
            var method = typeof(PodmanCliContainerDriver).GetMethod(
                "ParseStatsOutput",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (ContainerStatsResult)method.Invoke(null, new object[] { json });
        }

        private static string InvokeQuoteArgumentIfNeeded(string arg)
        {
            var method = typeof(PodmanCliContainerDriver).GetMethod(
                "QuoteArgumentIfNeeded",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (string)method.Invoke(null, new object[] { arg });
        }

        #endregion
    }
}
