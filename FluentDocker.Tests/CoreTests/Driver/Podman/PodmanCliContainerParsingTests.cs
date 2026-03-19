using System;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for enriched PodmanCliContainerDriver inspect parsing,
  /// covering Podman-specific format differences (PR #303 issues).
  /// Part 1: Container inspect, config, health, and state parsing.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliContainerParsingTests
  {
    #region ParseContainerInspect — Full Fields

    [Fact]
    public void ParseContainerInspect_FullPodmanJson_PopulatesAllFields()
    {
      var json = @"[{
                ""Id"": ""abc123def456"",
                ""Image"": ""sha256:abc123"",
                ""Name"": ""test-container"",
                ""Created"": ""2024-06-15T10:30:00.123456789Z"",
                ""ResolvConfPath"": ""/etc/resolv.conf"",
                ""HostnamePath"": ""/var/run/hostname"",
                ""HostsPath"": ""/var/run/hosts"",
                ""LogPath"": ""/var/log/containers/abc123.log"",
                ""RestartCount"": 3,
                ""Driver"": ""overlay"",
                ""Args"": [""--config"", ""/etc/app.conf""],
                ""State"": {
                    ""Status"": ""running"",
                    ""Running"": true,
                    ""Paused"": false,
                    ""Restarting"": false,
                    ""OOMKilled"": false,
                    ""Dead"": false,
                    ""Pid"": 12345,
                    ""ExitCode"": 0,
                    ""Error"": """",
                    ""StartedAt"": ""2024-06-15T10:30:01Z"",
                    ""FinishedAt"": ""0001-01-01T00:00:00Z""
                },
                ""Config"": {
                    ""Hostname"": ""abc123"",
                    ""User"": ""appuser"",
                    ""Env"": [""PATH=/usr/bin"", ""HOME=/home/app""],
                    ""Cmd"": [""serve"", ""--port"", ""8080""],
                    ""Image"": ""myapp:latest"",
                    ""WorkingDir"": ""/app"",
                    ""Entrypoint"": [""/entrypoint.sh""],
                    ""Labels"": { ""app"": ""web"", ""env"": ""prod"" },
                    ""StopSignal"": ""SIGTERM""
                },
                ""Mounts"": [{
                    ""Name"": ""vol1"",
                    ""Source"": ""/host/data"",
                    ""Destination"": ""/container/data"",
                    ""Driver"": ""local"",
                    ""Mode"": ""Z"",
                    ""RW"": true,
                    ""Propagation"": ""rprivate""
                }],
                ""NetworkSettings"": {
                    ""Bridge"": """",
                    ""SandboxKey"": ""/var/run/netns/abc123"",
                    ""Gateway"": ""10.88.0.1"",
                    ""IPAddress"": ""10.88.0.5"",
                    ""MacAddress"": ""02:42:0a:58:00:05"",
                    ""Ports"": {
                        ""8080/tcp"": [{ ""HostIp"": ""0.0.0.0"", ""HostPort"": ""80"" }]
                    },
                    ""Networks"": {
                        ""podman"": {
                            ""NetworkID"": ""net123"",
                            ""Gateway"": ""10.88.0.1"",
                            ""IPAddress"": ""10.88.0.5"",
                            ""IPPrefixLen"": 16,
                            ""MacAddress"": ""02:42:0a:58:00:05""
                        }
                    }
                }
            }]";

      var result = PodmanCliContainerDriver.ParseContainerInspect(json);

      Assert.Equal("abc123def456", result.Id);
      Assert.Equal("sha256:abc123", result.Image);
      Assert.Equal("test-container", result.Name);
      Assert.Equal("overlay", result.Driver);
      Assert.Equal(3, result.RestartCount);
      Assert.Equal("/etc/resolv.conf", result.ResolvConfPath);
      Assert.Equal("/var/run/hostname", result.HostnamePath);
      Assert.Equal("/var/run/hosts", result.HostsPath);
      Assert.Equal("/var/log/containers/abc123.log", result.LogPath);
      Assert.Equal(2024, result.Created.ToUniversalTime().Year);
      Assert.Equal(6, result.Created.ToUniversalTime().Month);
      Assert.Equal(15, result.Created.ToUniversalTime().Day);
      Assert.Equal(10, result.Created.ToUniversalTime().Hour);
      Assert.Equal(30, result.Created.ToUniversalTime().Minute);
      Assert.Equal(new[] { "--config", "/etc/app.conf" }, result.Args);
      Assert.Equal("running", result.State.Status);
      Assert.True(result.State.Running);
      Assert.Equal(12345, result.State.Pid);
      Assert.NotNull(result.Config);
      Assert.Equal("abc123", result.Config.Hostname);
      Assert.Equal(new[] { "/entrypoint.sh" }, result.Config.EntryPoint);
      Assert.Single(result.Mounts);
      Assert.NotNull(result.NetworkSettings);
      Assert.Equal("10.88.0.5", result.NetworkSettings.IPAddress);
    }

    [Fact]
    public void ParseContainerInspect_UppercaseID_MapsToId()
    {
      var json = @"[{ ""ID"": ""upper123"", ""Name"": ""test"" }]";
      var result = PodmanCliContainerDriver.ParseContainerInspect(json);
      Assert.Equal("upper123", result.Id);
    }

    [Fact]
    public void ParseContainerInspect_MissingOptionalSections_GracefulNulls()
    {
      var json = @"{ ""Id"": ""abc"", ""Name"": ""minimal"" }";
      var result = PodmanCliContainerDriver.ParseContainerInspect(json);

      Assert.Equal("abc", result.Id);
      Assert.Null(result.Config);
      Assert.Null(result.NetworkSettings);
      Assert.Empty(result.Mounts);
      Assert.NotNull(result.State);
    }

    #endregion

    #region ParseContainerConfig — EntryPoint Array vs String

    [Fact]
    public void ParseContainerConfig_EntryPointAsArray_ReturnsStringArray()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{ ""Entrypoint"": [""/bin/sh"", ""-c""] }");
      var config = PodmanCliContainerDriver.ParseContainerConfig(token);
      Assert.Equal(new[] { "/bin/sh", "-c" }, config.EntryPoint);
    }

    [Fact]
    public void ParseContainerConfig_EntryPointAsSingleString_WrapsInArray()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{ ""Entrypoint"": ""/entrypoint.sh"" }");
      var config = PodmanCliContainerDriver.ParseContainerConfig(token);
      Assert.Equal(new[] { "/entrypoint.sh" }, config.EntryPoint);
    }

    [Fact]
    public void ParseContainerConfig_EntryPointMissing_ReturnsNull()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{ ""Image"": ""alpine"" }");
      var config = PodmanCliContainerDriver.ParseContainerConfig(token);
      Assert.Null(config.EntryPoint);
    }

    [Fact]
    public void ParseContainerConfig_CmdAsString_WrapsInArray()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{ ""Cmd"": ""run-server"" }");
      var config = PodmanCliContainerDriver.ParseContainerConfig(token);
      Assert.Equal(new[] { "run-server" }, config.Cmd);
    }

    [Fact]
    public void ParseContainerConfig_AllFields_Populated()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{
                ""Hostname"": ""myhost"",
                ""DomainName"": ""example.com"",
                ""User"": ""root"",
                ""AttachStdin"": false,
                ""AttachStdout"": true,
                ""AttachStderr"": true,
                ""Tty"": true,
                ""OpenStdin"": true,
                ""StdinOnce"": false,
                ""Image"": ""nginx:latest"",
                ""WorkingDir"": ""/var/www"",
                ""StopSignal"": ""SIGQUIT"",
                ""Env"": [""FOO=bar"", ""BAZ=qux""],
                ""Cmd"": [""nginx"", ""-g"", ""daemon off;""],
                ""Entrypoint"": [""/docker-entrypoint.sh""],
                ""ExposedPorts"": { ""80/tcp"": {}, ""443/tcp"": {} },
                ""Labels"": { ""maintainer"": ""ops"" }
            }");

      var config = PodmanCliContainerDriver.ParseContainerConfig(token);

      Assert.Equal("myhost", config.Hostname);
      Assert.Equal("example.com", config.DomainName);
      Assert.Equal("root", config.User);
      Assert.False(config.AttachStdin);
      Assert.True(config.AttachStdout);
      Assert.True(config.Tty);
      Assert.Equal("nginx:latest", config.Image);
      Assert.Equal("/var/www", config.WorkingDir);
      Assert.Equal("SIGQUIT", config.StopSignal);
      Assert.Equal(new[] { "FOO=bar", "BAZ=qux" }, config.Env);
      Assert.Equal(2, config.ExposedPorts.Count);
      Assert.Equal("ops", config.Labels["maintainer"]);
    }

    [Fact]
    public void ParseContainerConfig_NullToken_ReturnsNull()
    {
      Assert.Null(PodmanCliContainerDriver.ParseContainerConfig(null));
    }

    [Fact]
    public void ParseContainerConfig_PodmanEntryPointKey_Parsed()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{ ""EntryPoint"": [""/start.sh""] }");
      var config = PodmanCliContainerDriver.ParseContainerConfig(token);
      Assert.Equal(new[] { "/start.sh" }, config.EntryPoint);
    }

    #endregion

    #region ParseHealth — Healthcheck Key and Empty Status

    [Fact]
    public void ParseHealth_HealthyStatus_ReturnsHealthy()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{
                ""Status"": ""healthy"",
                ""FailingStreak"": 0,
                ""Log"": [{
                    ""Start"": ""2024-06-15T10:00:00Z"",
                    ""End"": ""2024-06-15T10:00:01Z"",
                    ""ExitCode"": 0,
                    ""Output"": ""OK""
                }]
            }");

      var health = PodmanCliContainerDriver.ParseHealth(token);

      Assert.NotNull(health);
      Assert.Equal(HealthState.Healthy, health.Status);
      Assert.Equal(0, health.FailingStreak);
      Assert.Single(health.Log);
      Assert.Equal(0, health.Log[0].ExitCode);
      Assert.Equal("OK", health.Log[0].Output);
    }

    [Fact]
    public void ParseHealth_EmptyStatus_MapsToUnknown()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{ ""Status"": """" }");
      var health = PodmanCliContainerDriver.ParseHealth(token);
      Assert.Equal(HealthState.Unknown, health.Status);
    }

    [Fact]
    public void ParseHealth_NullToken_ReturnsNull()
    {
      Assert.Null(PodmanCliContainerDriver.ParseHealth(null));
    }

    [Fact]
    public void ParseHealth_UnhealthyWithStreak_ParsedCorrectly()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{
                ""Status"": ""unhealthy"",
                ""FailingStreak"": 5,
                ""Log"": [
                    { ""ExitCode"": 1, ""Output"": ""fail1"" },
                    { ""ExitCode"": 1, ""Output"": ""fail2"" }
                ]
            }");

      var health = PodmanCliContainerDriver.ParseHealth(token);
      Assert.Equal(HealthState.Unhealthy, health.Status);
      Assert.Equal(5, health.FailingStreak);
      Assert.Equal(2, health.Log.Count);
    }

    [Fact]
    public void ParseHealth_UnknownStatusString_MapsToUnknown()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{ ""Status"": ""unexpected-value"" }");
      Assert.Equal(HealthState.Unknown,
          PodmanCliContainerDriver.ParseHealth(token).Status);
    }

    [Fact]
    public void ParseHealth_NoLogArray_LogIsNull()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{ ""Status"": ""starting"" }");
      var health = PodmanCliContainerDriver.ParseHealth(token);
      Assert.Equal(HealthState.Starting, health.Status);
      Assert.Null(health.Log);
    }

    #endregion

    #region ParseContainerState — Healthcheck Key Fallback

    [Fact]
    public void ParseContainerState_HealthKey_ParsesHealth()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{
                ""Status"": ""running"",
                ""Running"": true,
                ""Pid"": 999,
                ""ExitCode"": 0,
                ""StartedAt"": ""2024-06-15T10:00:00Z"",
                ""FinishedAt"": ""0001-01-01T00:00:00Z"",
                ""Health"": { ""Status"": ""healthy"", ""FailingStreak"": 0 }
            }");

      var state = PodmanCliContainerDriver.ParseContainerState(token);

      Assert.True(state.Running);
      Assert.Equal(999, state.Pid);
      Assert.NotNull(state.Health);
      Assert.Equal(HealthState.Healthy, state.Health.Status);
      var startedUtc = state.StartedAt.ToUniversalTime();
      Assert.Equal(2024, startedUtc.Year);
      Assert.Equal(10, startedUtc.Hour);
    }

    [Fact]
    public void ParseContainerState_HealthcheckKey_FallbackParsesHealth()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{
                ""Status"": ""running"",
                ""Running"": true,
                ""Healthcheck"": { ""Status"": ""unhealthy"", ""FailingStreak"": 3 }
            }");

      var state = PodmanCliContainerDriver.ParseContainerState(token);
      Assert.NotNull(state.Health);
      Assert.Equal(HealthState.Unhealthy, state.Health.Status);
      Assert.Equal(3, state.Health.FailingStreak);
    }

    [Fact]
    public void ParseContainerState_AllFields_Populated()
    {
      JsonElement? token = JsonHelper.ParseElement(@"{
                ""Status"": ""exited"",
                ""Running"": false,
                ""Paused"": false,
                ""Restarting"": false,
                ""OOMKilled"": true,
                ""Dead"": true,
                ""Pid"": 0,
                ""ExitCode"": 137,
                ""Error"": ""OOM killed"",
                ""StartedAt"": ""2024-06-15T10:00:00Z"",
                ""FinishedAt"": ""2024-06-15T10:05:00Z""
            }");

      var state = PodmanCliContainerDriver.ParseContainerState(token);

      Assert.Equal("exited", state.Status);
      Assert.True(state.OOMKilled);
      Assert.True(state.Dead);
      Assert.Equal(137, state.ExitCode);
      Assert.Equal("OOM killed", state.Error);
    }

    #endregion

    #region ParseStringOrArray Helper

    [Fact]
    public void ParseStringOrArray_Array_ReturnsArray()
    {
      JsonElement? token = JsonHelper.ParseElement(@"[""/bin/sh"", ""-c""]");
      Assert.Equal(new[] { "/bin/sh", "-c" },
          PodmanCliContainerDriver.ParseStringOrArray(token));
    }

    [Fact]
    public void ParseStringOrArray_String_WrapsInArray()
    {
      // Parse a JSON string value; ParseElement will give us a JsonElement of type String
      JsonElement? token = JsonHelper.ParseElement(@"""/entrypoint.sh""");
      Assert.Equal(new[] { "/entrypoint.sh" },
          PodmanCliContainerDriver.ParseStringOrArray(token));
    }

    [Fact]
    public void ParseStringOrArray_Null_ReturnsNull()
    {
      Assert.Null(PodmanCliContainerDriver.ParseStringOrArray(null));
    }

    #endregion
  }
}
