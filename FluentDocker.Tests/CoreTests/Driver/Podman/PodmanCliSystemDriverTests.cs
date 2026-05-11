using System.Reflection;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for PodmanCliSystemDriver JSON parsing.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliSystemDriverTests
  {
    #region ParseSystemInfo Tests

    [Fact]
    public void ParseSystemInfo_ValidJson_ParsesHostSection()
    {
      var json = @"{
                ""host"": {
                    ""os"": ""linux"",
                    ""arch"": ""amd64"",
                    ""hostname"": ""podman-host"",
                    ""kernel"": ""5.15.0-76-generic"",
                    ""cpus"": 8,
                    ""memTotal"": 16777216000,
                    ""conmon"": { ""version"": ""2.1.7"" }
                },
                ""store"": {
                    ""graphDriverName"": ""overlay"",
                    ""graphRoot"": ""/var/lib/containers/storage"",
                    ""imageStore"": { ""number"": 12 }
                }
            }";

      var result = InvokeParseSystemInfo(json);
      Assert.Equal("linux", result.OperatingSystem);
      Assert.Equal("amd64", result.Architecture);
      Assert.Equal("podman-host", result.Hostname);
      Assert.Equal("5.15.0-76-generic", result.KernelVersion);
      Assert.Equal(8, result.CPUs);
      Assert.Equal(16777216000, result.MemoryTotal);
      Assert.Equal("overlay", result.StorageBackend);
      Assert.Equal("/var/lib/containers/storage", result.DataRoot);
      Assert.Equal(12, result.Images);
      Assert.Equal("linux", result.OSType);
    }

    [Fact]
    public void ParseSystemInfo_WithVersionSection_OverridesEngineVersion()
    {
      var json = @"{
                ""host"": { ""conmon"": { ""version"": ""2.1.7"" } },
                ""version"": { ""Version"": ""4.5.0"" }
            }";

      var result = InvokeParseSystemInfo(json);
      Assert.Equal("4.5.0", result.EngineVersion);
    }

    [Fact]
    public void ParseSystemInfo_EmptyJson_ReturnsEmptyInfo()
    {
      var result = InvokeParseSystemInfo("{}");
      Assert.NotNull(result);
      Assert.Equal("linux", result.OSType);
    }

    [Fact]
    public void ParseSystemInfo_InvalidJson_ReturnsEmptyInfo()
    {
      var result = InvokeParseSystemInfo("not json");
      Assert.NotNull(result);
    }

    #endregion

    #region ParseVersionInfo Tests

    [Fact]
    public void ParseVersionInfo_WithClientAndServer_ParsesBoth()
    {
      var json = @"{
                ""Client"": {
                    ""Version"": ""4.5.0"",
                    ""APIVersion"": ""4.5.0"",
                    ""GitCommit"": ""abc123"",
                    ""GoVersion"": ""go1.21"",
                    ""Os"": ""linux"",
                    ""Arch"": ""amd64"",
                    ""Built"": ""2024-01-15""
                },
                ""Server"": {
                    ""Version"": ""4.5.0"",
                    ""APIVersion"": ""4.5.0""
                }
            }";

      var result = InvokeParseVersionInfo(json);
      Assert.Equal("4.5.0", result.ClientVersion);
      Assert.Equal("4.5.0", result.ClientApiVersion);
      Assert.Equal("abc123", result.GitCommit);
      Assert.Equal("go1.21", result.RuntimeVersion);
      Assert.Equal("linux", result.Os);
      Assert.Equal("amd64", result.Arch);
      Assert.Equal("4.5.0", result.ServerVersion);
      Assert.Equal("4.5.0", result.ServerApiVersion);
      Assert.Equal("Podman", result.PlatformName);
    }

    [Fact]
    public void ParseVersionInfo_RootlessMode_NoServer_CopiesClientVersion()
    {
      var json = @"{
                ""Client"": {
                    ""Version"": ""4.5.0"",
                    ""APIVersion"": ""4.5.0""
                }
            }";

      var result = InvokeParseVersionInfo(json);
      Assert.Equal("4.5.0", result.ClientVersion);
      Assert.Equal("4.5.0", result.ServerVersion);
    }

    [Fact]
    public void ParseVersionInfo_FlatStructure_ParsesAsClient()
    {
      var json = @"{""Version"":""4.5.0"",""APIVersion"":""4.5.0"",""Os"":""linux""}";

      var result = InvokeParseVersionInfo(json);
      Assert.Equal("4.5.0", result.ClientVersion);
      Assert.Equal("Podman", result.PlatformName);
    }

    [Fact]
    public void ParseVersionInfo_InvalidJson_ReturnsEmptyVersion()
    {
      var result = InvokeParseVersionInfo("not json");
      Assert.NotNull(result);
    }

    #endregion

    #region Daemon Switching Tests

    [Fact]
    public async Task SwitchDaemonAsync_ReturnsCapabilityNotSupported()
    {
      var driver = new PodmanCliSystemDriver(null);

      var result = await driver.SwitchDaemonAsync(new DriverContext("podman"), TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Driver.CapabilityNotSupported, result.ErrorCode);
    }

    [Fact]
    public async Task SwitchToLinuxDaemonAsync_ReturnsCapabilityNotSupported()
    {
      var driver = new PodmanCliSystemDriver(null);

      var result = await driver.SwitchToLinuxDaemonAsync(new DriverContext("podman"), TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Driver.CapabilityNotSupported, result.ErrorCode);
    }

    #endregion

    #region Reflection Helpers

    private static SystemInfo InvokeParseSystemInfo(string json)
    {
      var method = typeof(PodmanCliSystemDriver).GetMethod(
          "ParseSystemInfo",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (SystemInfo)method.Invoke(null, [json]);
    }

    private static VersionInfo InvokeParseVersionInfo(string json)
    {
      var method = typeof(PodmanCliSystemDriver).GetMethod(
          "ParseVersionInfo",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (VersionInfo)method.Invoke(null, [json]);
    }

    #endregion
  }
}
