using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiContainerApiIssueTests
  {
    private static DriverContext Ctx => new("docker-api-issues-test");

    private static DockerApiContainerDriver CreateDriver(MockDockerApiConnection conn)
    {
      var driver = new DockerApiContainerDriver(conn);
      driver.Initialize(Ctx);
      return driver;
    }

    [Fact]
    public async Task GetLogsAsync_FollowTrue_FailsWithoutOpeningStream()
    {
      var mock = new MockDockerApiConnection();
      var driver = CreateDriver(mock);

      var result = await driver.GetLogsAsync(
          Ctx, "ctr1", follow: true, cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.LogsFailed, result.ErrorCode);
      Assert.Contains("IStreamDriver.StreamLogsAsync", result.Error);
      Assert.Empty(mock.GetRequests());
    }

    [Fact]
    public async Task ExecAsync_Interactive_FailsWithoutCreatingExec()
    {
      var mock = new MockDockerApiConnection();
      var driver = CreateDriver(mock);

      var result = await driver.ExecAsync(Ctx, "ctr1",
          new ExecConfig { Command = ["cat"], Interactive = true },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.ExecFailed, result.ErrorCode);
      Assert.Contains("interactive stdin is not supported by the Docker API driver", result.Error);
      Assert.Empty(mock.GetRequests());
    }

    [Fact]
    public async Task CopyToAsync_FileTarMode_IsNotWorldWritable()
    {
      var root = CreateOutDirectory("copyto-mode");
      var source = Path.Combine(root, "secret.txt");
      await File.WriteAllTextAsync(source, "secret", TestContext.Current.CancellationToken);
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite);

      var mock = new MockDockerApiConnection();
      mock.SetupPut("/archive", 200, "{}");
      var driver = CreateDriver(mock);

      var result = await driver.CopyToAsync(
          Ctx, "ctr1", source, "/root/secret.txt", TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var request = mock.GetRequests().Single(r => r.Method == "PUT");
      Assert.NotNull(request.BodyBytes);
      Assert.NotEqual(511, ReadFirstTarMode(request.BodyBytes!));
    }

    [Fact]
    public async Task CreateAsync_HealthcheckParsesCompoundAndHourDurations()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/containers/create", 201, @"{""Id"":""ctr1""}");
      var driver = CreateDriver(mock);

      var result = await driver.CreateAsync(Ctx, new ContainerCreateConfig
      {
        Image = "busybox",
        HealthCheck = new HealthCheckConfig
        {
          Test = ["CMD", "true"],
          Interval = "1m30s",
          Timeout = "1h",
          StartPeriod = "250ms"
        }
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var body = mock.GetRequests().Single(r => r.Method == "POST").Body;
      Assert.Contains(@"""Interval"":90000000000", body);
      Assert.Contains(@"""Timeout"":3600000000000", body);
      Assert.Contains(@"""StartPeriod"":250000000", body);
    }

    [Fact]
    public async Task CreateAsync_WithPlatform_SendsPlatformAsQueryParameter()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/containers/create", 201, @"{""Id"":""ctr1""}");
      var driver = CreateDriver(mock);

      var result = await driver.CreateAsync(Ctx, new ContainerCreateConfig
      {
        Image = "busybox",
        Platform = "linux/arm64"
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var request = mock.GetRequests().Single(r => r.Method == "POST");
      Assert.Contains("platform=linux%2Farm64", request.Path);
      Assert.DoesNotContain(@"""Platform"":""linux/arm64""", request.Body);
    }

    [Fact]
    public async Task CreateAsync_WithHostIpPortBinding_SplitsHostIpAndHostPort()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/containers/create", 201, @"{""Id"":""ctr1""}");
      var driver = CreateDriver(mock);

      var result = await driver.CreateAsync(Ctx, new ContainerCreateConfig
      {
        Image = "busybox",
        PortBindings = { ["80/tcp"] = "127.0.0.1:8080" }
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var body = mock.GetRequests().Single(r => r.Method == "POST").Body;
      Assert.Contains(@"""HostIp"":""127.0.0.1""", body);
      Assert.Contains(@"""HostPort"":""8080""", body);
      Assert.DoesNotContain(@"""HostPort"":""127.0.0.1:8080""", body);
    }

    [Fact]
    public async Task CreateAsync_WithStaticIpOnMultipleNetworks_AppliesIpamOnlyToPrimaryNetwork()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/containers/create", 201, @"{""Id"":""ctr1""}");
      var driver = CreateDriver(mock);

      var result = await driver.CreateAsync(Ctx, new ContainerCreateConfig
      {
        Image = "busybox",
        Networks = ["primary", "secondary"],
        Ipv4Address = "172.20.0.10"
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var body = mock.GetRequests().Single(r => r.Method == "POST").Body;
      Assert.Equal(1, CountOccurrences(body ?? string.Empty, @"""IPv4Address"":""172.20.0.10"""));
    }

    private static string CreateOutDirectory(string name)
    {
      var path = Path.GetFullPath(Path.Combine(".out", "docker-api-tests", name, Guid.NewGuid().ToString("N")));
      Directory.CreateDirectory(path);
      return path;
    }

    private static int ReadFirstTarMode(byte[] tarBytes)
    {
      var modeText = System.Text.Encoding.ASCII.GetString(tarBytes, 100, 8).Trim(' ', '\0');
      return Convert.ToInt32(modeText, 8);
    }

    private static int CountOccurrences(string value, string needle)
    {
      var count = 0;
      var index = 0;
      while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
      {
        count++;
        index += needle.Length;
      }
      return count;
    }
  }
}
