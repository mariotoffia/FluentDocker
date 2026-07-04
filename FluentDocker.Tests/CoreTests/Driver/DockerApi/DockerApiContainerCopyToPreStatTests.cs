using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  public partial class DockerApiContainerOperationsTests
  {
    [Fact]
    public async Task CopyToAsync_FileTargetDirectory_StatsWithHeadAndCopiesIntoDirectory()
    {
      var hostFile = Path.Combine(".out", "docker-api-copyto-head", "file.txt");
      Directory.CreateDirectory(Path.GetDirectoryName(hostFile)!);
      await File.WriteAllTextAsync(hostFile, "payload", TestContext.Current.CancellationToken);
      var mock = new MockDockerApiConnection();
      mock.SetupHead("/containers/ctr1/archive", 200, StatHeaders(0x800001ED));
      mock.SetupPut("/containers/ctr1/archive", 200, "{}");
      var driver = CreateDriver(mock);

      var result = await driver.CopyToAsync(
          Ctx, "ctr1", hostFile, "/etc", TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var requests = mock.GetRequests();
      Assert.Contains(requests, static r => r.Method == "HEAD" && r.Path.Contains("path=%2Fetc"));
      Assert.DoesNotContain(requests, static r => r.Method == "GET" && r.Path.Contains("/archive"));
      Assert.Contains(requests, static r => r.Method == "PUT" && r.Path.Contains("path=%2Fetc"));
    }

    [Fact]
    public async Task CopyToAsync_FileTargetExistingFile_StatsWithHeadAndCopiesToParent()
    {
      var hostFile = Path.Combine(".out", "docker-api-copyto-head-file", "file.txt");
      Directory.CreateDirectory(Path.GetDirectoryName(hostFile)!);
      await File.WriteAllTextAsync(hostFile, "payload", TestContext.Current.CancellationToken);
      var mock = new MockDockerApiConnection();
      mock.SetupHead("/containers/ctr1/archive", 200, StatHeaders(0x000001A4));
      mock.SetupPut("/containers/ctr1/archive", 200, "{}");
      var driver = CreateDriver(mock);

      var result = await driver.CopyToAsync(
          Ctx, "ctr1", hostFile, "/etc", TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Contains(mock.GetRequests(),
          static r => r.Method == "PUT" && r.Path.Contains("path=%2F"));
    }

    private static IReadOnlyDictionary<string, string> StatHeaders(long mode)
    {
      var json = $$"""{"name":"target","size":0,"mode":{{mode}},"mtime":"2026-01-01T00:00:00Z","linkTarget":""}""";
      return new Dictionary<string, string>
      {
        ["X-Docker-Container-Path-Stat"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
      };
    }
  }
}
