using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class ProdReadyDockerApiFixesTests
  {
    private static DriverContext Ctx => new("docker-api-prod-ready-fixes-test");

    [Fact]
    public async Task BuildAsync_RegistryConfigHeader_IncludesDockerHubPasswordAndServerAddress()
    {
      var contextPath = CreateOutDirectory("registry-auth");
      try
      {
        File.WriteAllText(Path.Combine(contextPath, "Dockerfile"), "FROM private/base:latest\n");
        var mock = new MockDockerApiConnection();
        mock.SetupPost("/auth", 200, @"{""Status"":""Login Succeeded""}");
        mock.SetupStream("/build", "{\"aux\":{\"ID\":\"sha256:test\"}}\n");
        var auth = new DockerApiAuthDriver(mock);
        auth.Initialize(Ctx);
        var image = new DockerApiImageDriver(mock);
        image.Initialize(Ctx);

        var login = await auth.LoginAsync(Ctx, new RegistryLoginConfig
        {
          Username = "user",
          Password = "secret",
          Email = "user@example.invalid"
        }, TestContext.Current.CancellationToken);
        var result = await image.BuildAsync(Ctx,
            new ImageBuildConfig { BuildContext = contextPath }, null!,
            TestContext.Current.CancellationToken);

        Assert.True(login.Success, login.Error);
        Assert.True(result.Success, result.Error);
        var request = mock.GetRequests().Single(r => r.Method == "POST_STREAM");
        Assert.NotNull(request.Headers);
        Assert.True(request.Headers!.TryGetValue("X-Registry-Config", out var encoded));
        using var json = JsonDocument.Parse(DecodeBase64Url(encoded!));
        Assert.True(json.RootElement.TryGetProperty("https://index.docker.io/v1/", out var hub),
            json.RootElement.ToString());
        Assert.Equal("user", hub.GetProperty("username").GetString());
        Assert.Equal("secret", hub.GetProperty("password").GetString());
        Assert.Equal("user@example.invalid", hub.GetProperty("email").GetString());
        Assert.Equal("https://index.docker.io/v1/", hub.GetProperty("serveraddress").GetString());
      }
      finally
      {
        Directory.Delete(contextPath, recursive: true);
      }
    }

    [Fact]
    public async Task ExecAsync_SystemerrFrameType_RoutesPayloadToStdErr()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-systemerr""}");
      mock.SetupStreamBytes("/exec/exec-systemerr/start",
          Frame(3, Encoding.UTF8.GetBytes("daemon-side error\n")));
      mock.SetupGet("/exec/exec-systemerr/json", 200, @"{""Running"":false,""ExitCode"":0}");
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ExecAsync(Ctx, "ctr",
          new ExecConfig { Command = ["true"] }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(string.Empty, result.Data.StdOut);
      Assert.Contains("daemon-side error", result.Data.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_LongSymlinkTarget_EmitsGnuLongLinknameBlock()
    {
      var contextPath = CreateOutDirectory("long-symlink");
      try
      {
        File.WriteAllText(Path.Combine(contextPath, "Dockerfile"), "FROM scratch\n");
        var a = new string('a', 60);
        var b = new string('b', 60);
        var targetDir = Path.Combine(contextPath, a, b);
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "target.txt"), "target");
        var linkTarget = $"{a}/{b}/target.txt";
        Assert.True(Encoding.UTF8.GetByteCount(linkTarget) > 100);
        try
        {
          File.CreateSymbolicLink(Path.Combine(contextPath, "link.txt"), linkTarget);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
          Assert.Skip("Symlink creation is not permitted on this platform.");
          return;
        }
        var mock = new MockDockerApiConnection();
        mock.SetupStream("/build", "{\"aux\":{\"ID\":\"sha256:test\"}}\n");
        var driver = new DockerApiImageDriver(mock);
        driver.Initialize(Ctx);

        var result = await driver.BuildAsync(Ctx,
            new ImageBuildConfig { BuildContext = contextPath }, null!,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var body = mock.GetRequests().Single(r => r.Method == "POST_STREAM").BodyBytes!;
        var types = ReadTarEntryTypes(body);
        Assert.Contains((byte)'K', types);
        Assert.Contains((byte)'2', types);
      }
      finally
      {
        Directory.Delete(contextPath, recursive: true);
      }
    }

    private static string CreateOutDirectory(string name)
    {
      var path = Path.GetFullPath(Path.Combine(
          ".out", "docker-api-prod-ready-fixes", name, Guid.NewGuid().ToString("N")));
      Directory.CreateDirectory(path);
      return path;
    }

    private static string DecodeBase64Url(string value)
    {
      var base64 = value.Replace('-', '+').Replace('_', '/');
      base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
      return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static byte[] Frame(byte streamType, byte[] payload)
    {
      var frame = new byte[8 + payload.Length];
      frame[0] = streamType;
      frame[4] = (byte)((payload.Length >> 24) & 0xFF);
      frame[5] = (byte)((payload.Length >> 16) & 0xFF);
      frame[6] = (byte)((payload.Length >> 8) & 0xFF);
      frame[7] = (byte)(payload.Length & 0xFF);
      Array.Copy(payload, 0, frame, 8, payload.Length);
      return frame;
    }

    private static List<byte> ReadTarEntryTypes(byte[] bytes)
    {
      var types = new List<byte>();
      using var stream = new MemoryStream(bytes);
      var header = new byte[512];
      while (ReadExactly(stream, header))
      {
        if (Array.TrueForAll(header, b => b == 0))
          break;
        types.Add(header[156]);
        var sizeField = Encoding.ASCII.GetString(header, 124, 12).Trim(' ', '\0');
        var size = sizeField.Length == 0 ? 0 : Convert.ToInt64(sizeField, 8);
        for (var i = (size + 511) / 512; i > 0; i--)
          if (!ReadExactly(stream, header))
            return types;
      }
      return types;
    }

    private static bool ReadExactly(Stream stream, byte[] buffer)
    {
      var read = 0;
      while (read < buffer.Length)
      {
        var count = stream.Read(buffer, read, buffer.Length - read);
        if (count == 0)
          return false;
        read += count;
      }
      return true;
    }
  }
}
