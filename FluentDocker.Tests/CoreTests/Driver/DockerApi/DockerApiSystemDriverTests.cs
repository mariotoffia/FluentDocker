using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiSystemDriverTests
  {
    private static DriverContext Ctx => new("docker-api-test");

    private static (DockerApiSystemDriver driver, MockDockerApiConnection mock) CreateDriver()
    {
      var mock = new MockDockerApiConnection();
      var driver = new DockerApiSystemDriver(mock);
      driver.Initialize(new DriverContext("docker-api-test"));
      return (driver, mock);
    }

    [Fact]
    public async Task GetInfoAsync_ReturnsSystemInfo_WithAllFields()
    {
      const string json =
          @"{""OperatingSystem"":""Docker Desktop"",""OSType"":""linux"",""OSVersion"":""5.15.0"","
          + @"""Architecture"":""x86_64"",""Containers"":5,""ContainersRunning"":2,"
          + @"""ContainersPaused"":1,""ContainersStopped"":2,""Images"":42,"
          + @"""ServerVersion"":""24.0.7"",""Driver"":""overlay2"",""LoggingDriver"":""json-file"","
          + @"""KernelVersion"":""5.15.49-linuxkit"",""MemTotal"":8345890816,""NCPU"":4,"
          + @"""DockerRootDir"":""/var/lib/docker"",""Name"":""docker-desktop"","
          + @"""DefaultRuntime"":""runc""}";
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/info", 200, json);

      var result = await driver.GetInfoAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal("Docker Desktop", result.Data.OperatingSystem);
      Assert.Equal("linux", result.Data.OSType);
      Assert.Equal("5.15.0", result.Data.OSVersion);
      Assert.Equal("x86_64", result.Data.Architecture);
      Assert.Equal(5, result.Data.Containers);
      Assert.Equal(2, result.Data.ContainersRunning);
      Assert.Equal(1, result.Data.ContainersPaused);
      Assert.Equal(2, result.Data.ContainersStopped);
      Assert.Equal(42, result.Data.Images);
      Assert.Equal("24.0.7", result.Data.EngineVersion);
      Assert.Equal("overlay2", result.Data.StorageBackend);
      Assert.Equal("json-file", result.Data.LoggingBackend);
      Assert.Equal("5.15.49-linuxkit", result.Data.KernelVersion);
      Assert.Equal(8345890816L, result.Data.MemoryTotal);
      Assert.Equal(4, result.Data.CPUs);
      Assert.Equal("/var/lib/docker", result.Data.DataRoot);
      Assert.Equal("docker-desktop", result.Data.Hostname);
      Assert.Equal("runc", result.Data.DefaultRuntime);
    }

    [Fact]
    public async Task GetInfoAsync_PopulatesMetaInfo()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/info", 200,
          @"{""OperatingSystem"":""Docker Desktop"",""OSType"":""linux"",""NCPU"":8}");

      var result = await driver.GetInfoAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal("Docker Desktop", result.Data.MetaInfo[SystemInfoMetaKeys.OperatingSystem]);
      Assert.Equal("linux", result.Data.MetaInfo[SystemInfoMetaKeys.OSType]);
      Assert.Equal("8", result.Data.MetaInfo[SystemInfoMetaKeys.Cpus]);
    }

    [Fact]
    public async Task GetInfoAsync_FailsOnServerError()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/info", 500, @"{""message"":""internal error""}");

      var result = await driver.GetInfoAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
      Assert.Contains("internal error", result.Error);
      Assert.Equal(ErrorCodes.Api.ServerError, result.ErrorCode);
    }

    [Fact]
    public async Task GetVersionAsync_ReturnsVersionInfo_WithAllFields()
    {
      const string json =
          @"{""Version"":""24.0.7"",""ApiVersion"":""1.43"",""MinAPIVersion"":""1.24"","
          + @"""GitCommit"":""afdd53b"",""GoVersion"":""go1.20.10"",""Os"":""linux"","
          + @"""Arch"":""amd64"",""BuildTime"":""2023-10-26T09:08:00.000000000+00:00"","
          + @"""Experimental"":true,""Platform"":{""Name"":""Docker Engine - Community""}}";
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/version", 200, json);

      var result = await driver.GetVersionAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal("24.0.7", result.Data.ServerVersion);
      Assert.Equal("1.43", result.Data.ServerApiVersion);
      Assert.Equal("1.24", result.Data.MinApiVersion);
      Assert.Equal("afdd53b", result.Data.GitCommit);
      Assert.Equal("go1.20.10", result.Data.RuntimeVersion);
      Assert.Equal("linux", result.Data.Os);
      Assert.Equal("amd64", result.Data.Arch);
      Assert.True(result.Data.Experimental);
      Assert.Equal("Docker Engine - Community", result.Data.PlatformName);
    }

    [Fact]
    public async Task GetVersionAsync_SetsClientVersionToServerVersion()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/version", 200, @"{""Version"":""24.0.7"",""ApiVersion"":""1.43""}");

      var result = await driver.GetVersionAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal(result.Data.ServerVersion, result.Data.ClientVersion);
      Assert.Equal(result.Data.ServerApiVersion, result.Data.ClientApiVersion);
    }

    [Fact]
    public async Task GetVersionAsync_FailsOnUnauthorized()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/version", 401, @"{""message"":""unauthorized""}");

      var result = await driver.GetVersionAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Api.Unauthorized, result.ErrorCode);
    }

    [Fact]
    public async Task PingAsync_ReturnsOk_WhenDaemonResponds()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPing(true);
      Assert.True((await driver.PingAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task PingAsync_ReturnsFail_WhenDaemonUnresponsive()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPing(false);

      var result = await driver.PingAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Driver.NotAvailable, result.ErrorCode);
      Assert.Contains("not responding", result.Error);
    }

    [Fact]
    public async Task GetDiskUsageAsync_ParsesImagesContainersVolumes()
    {
      const string json =
          @"{""Images"":[{""Size"":100000},{""Size"":200000}],"
          + @"""Containers"":[{""SizeRw"":5000},{""SizeRw"":3000},{""SizeRw"":2000}],"
          + @"""Volumes"":[{""UsageData"":{""Size"":50000,""RefCount"":1}}]}";
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/system/df", 200, json);

      var result = await driver.GetDiskUsageAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal(2, result.Data.Images.TotalCount);
      Assert.Equal(300000L, result.Data.Images.Size);
      Assert.Equal(3, result.Data.Containers.TotalCount);
      Assert.Equal(10000L, result.Data.Containers.Size);
      Assert.Equal(1, result.Data.Volumes.TotalCount);
      Assert.Equal(50000L, result.Data.Volumes.Size);
      Assert.Equal(360000L, result.Data.TotalSize);
    }

    [Fact]
    public async Task GetDiskUsageAsync_HandlesEmptyArrays()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/system/df", 200,
          @"{""Images"":[],""Containers"":[],""Volumes"":[]}");

      var result = await driver.GetDiskUsageAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal(0, result.Data.Images.TotalCount);
      Assert.Equal(0L, result.Data.Containers.Size);
      Assert.Equal(0L, result.Data.TotalSize);
    }

    [Fact]
    public async Task GetDiskUsageAsync_HandlesMissingArrays()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/system/df", 200, "{}");

      var result = await driver.GetDiskUsageAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal(0, result.Data.Images.TotalCount);
      Assert.Equal(0, result.Data.Containers.TotalCount);
      Assert.Equal(0, result.Data.Volumes.TotalCount);
      Assert.Equal(0L, result.Data.TotalSize);
    }

    [Fact]
    public async Task PruneAsync_DefaultConfig_CallsContainersNetworksImagesBuild()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/containers/prune", 200,
          @"{""ContainersDeleted"":[""c1"",""c2""],""SpaceReclaimed"":1000}");
      mock.SetupPost("/networks/prune", 200,
          @"{""NetworksDeleted"":[""net1""]}");
      mock.SetupPost("/images/prune", 200,
          @"{""ImagesDeleted"":[{""Untagged"":""img1""}],""SpaceReclaimed"":2000}");
      mock.SetupPost("/build/prune", 200,
          @"{""CachesDeleted"":[""cache1""],""SpaceReclaimed"":500}");

      var result = await driver.PruneAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(new[] { "c1", "c2" }, result.Data.ContainersDeleted);
      Assert.Equal(new[] { "net1" }, result.Data.NetworksDeleted);
      Assert.Single(result.Data.ImagesDeleted);
      Assert.Contains("img1", result.Data.ImagesDeleted);
      Assert.Empty(result.Data.VolumesDeleted); // volumes not pruned by default
      Assert.Equal(new[] { "cache1" }, result.Data.BuildCacheDeleted);
      Assert.Equal(3500L, result.Data.SpaceReclaimed);

      // Verify no volumes/prune call was made
      var requests = mock.GetRequests();
      Assert.DoesNotContain(requests,
          r => r.Method == "POST" && r.Path.Contains("/volumes/prune"));
    }

    [Fact]
    public async Task PruneAsync_WithVolumes_CallsVolumesPrune()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/containers/prune", 200, "{}");
      mock.SetupPost("/networks/prune", 200, "{}");
      mock.SetupPost("/images/prune", 200, "{}");
      mock.SetupPost("/build/prune", 200, "{}");
      mock.SetupPost("/volumes/prune", 200,
          @"{""VolumesDeleted"":[""vol1"",""vol2""],""SpaceReclaimed"":5000}");

      var config = new SystemPruneConfig { Volumes = true };
      var result = await driver.PruneAsync(Ctx, config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(new[] { "vol1", "vol2" }, result.Data.VolumesDeleted);
      Assert.Equal(5000L, result.Data.SpaceReclaimed);

      var requests = mock.GetRequests();
      Assert.Contains(requests,
          r => r.Method == "POST" && r.Path.Contains("/volumes/prune"));
    }

    [Fact]
    public async Task PruneAsync_WithAll_SetsImageDanglingFilter()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/containers/prune", 200, "{}");
      mock.SetupPost("/networks/prune", 200, "{}");
      mock.SetupPost("/images/prune", 200, "{}");
      mock.SetupPost("/build/prune", 200, "{}");

      var config = new SystemPruneConfig { All = true };
      var result = await driver.PruneAsync(Ctx, config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var requests = mock.GetRequests();
      var imgReq = requests.First(r =>
          r.Method == "POST" && r.Path.Contains("/images/prune"));
      Assert.Contains("dangling", imgReq.Path);
    }

    [Fact]
    public async Task PruneAsync_WithFilter_PassesFilterToEndpoints()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/containers/prune", 200, "{}");
      mock.SetupPost("/networks/prune", 200, "{}");
      mock.SetupPost("/images/prune", 200, "{}");
      mock.SetupPost("/build/prune", 200, "{}");

      var config = new SystemPruneConfig
      {
        Filter = new System.Collections.Generic.Dictionary<string, string>
        {
          ["until"] = "24h"
        }
      };
      var result = await driver.PruneAsync(Ctx, config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var requests = mock.GetRequests();
      var ctrReq = requests.First(r =>
          r.Method == "POST" && r.Path.Contains("/containers/prune"));
      Assert.Contains("until", ctrReq.Path);
    }

    [Fact]
    public async Task PruneAsync_PartialFailure_StillReturnsPartialResults()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/containers/prune", 200,
          @"{""ContainersDeleted"":[""c1""],""SpaceReclaimed"":100}");
      mock.SetupPost("/networks/prune", 500,
          @"{""message"":""network error""}");
      mock.SetupPost("/images/prune", 200,
          @"{""ImagesDeleted"":[{""Deleted"":""sha256:abc""}],""SpaceReclaimed"":200}");
      mock.SetupPost("/build/prune", 200, "{}");

      var result = await driver.PruneAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Single(result.Data.ContainersDeleted);
      Assert.Single(result.Data.ImagesDeleted);
      Assert.Empty(result.Data.NetworksDeleted); // failed endpoint
      Assert.Equal(300L, result.Data.SpaceReclaimed);
    }

    [Fact]
    public async Task PruneAsync_EmptyResponses_ReturnsEmptyLists()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/containers/prune", 200, "{}");
      mock.SetupPost("/networks/prune", 200, "{}");
      mock.SetupPost("/images/prune", 200, "{}");
      mock.SetupPost("/build/prune", 200, "{}");

      var result = await driver.PruneAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Empty(result.Data.ContainersDeleted);
      Assert.Empty(result.Data.NetworksDeleted);
      Assert.Empty(result.Data.ImagesDeleted);
      Assert.Empty(result.Data.VolumesDeleted);
      Assert.Empty(result.Data.BuildCacheDeleted);
      Assert.Equal(0L, result.Data.SpaceReclaimed);
    }

    [Theory]
    [InlineData("windows", true)]
    [InlineData("linux", false)]
    public async Task IsWindowsEngineAsync_ChecksOSType(string osType, bool expected)
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/info", 200, @"{""OSType"":""" + osType + @"""}");
      var result = await driver.IsWindowsEngineAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal(expected, result.Data);
    }

    [Theory]
    [InlineData("linux", true)]
    [InlineData("windows", false)]
    public async Task IsLinuxEngineAsync_ChecksOSType(string osType, bool expected)
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/info", 200, @"{""OSType"":""" + osType + @"""}");
      var result = await driver.IsLinuxEngineAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal(expected, result.Data);
    }

    [Fact]
    public async Task IsLinuxEngineAsync_PropagatesFailure()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/info", 500, @"{""message"":""server error""}");
      Assert.False((await driver.IsLinuxEngineAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task SwitchDaemonAsync_ReturnsCapabilityNotSupported()
    {
      var (driver, _) = CreateDriver();
      var r1 = await driver.SwitchDaemonAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      var r2 = await driver.SwitchToLinuxDaemonAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      var r3 = await driver.SwitchToWindowsDaemonAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(r1.Success);
      Assert.False(r2.Success);
      Assert.False(r3.Success);
      Assert.Equal(ErrorCodes.Driver.CapabilityNotSupported, r1.ErrorCode);
      Assert.Equal(ErrorCodes.Driver.CapabilityNotSupported, r2.ErrorCode);
      Assert.Equal(ErrorCodes.Driver.CapabilityNotSupported, r3.ErrorCode);
    }
  }
}
