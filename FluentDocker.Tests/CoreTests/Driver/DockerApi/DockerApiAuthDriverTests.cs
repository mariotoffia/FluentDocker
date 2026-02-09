using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiAuthDriverTests
  {
    private static DriverContext Ctx => new("docker-api-test");

    private static (DockerApiAuthDriver driver, MockDockerApiConnection mock) CreateDriver()
    {
      var mock = new MockDockerApiConnection();
      var driver = new DockerApiAuthDriver(mock);
      driver.Initialize(new DriverContext("docker-api-test"));
      return (driver, mock);
    }

    [Fact]
    public async Task LoginAsync_ReturnsSuccess()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/auth", 200,
          @"{""Status"":""Login Succeeded""}");

      var config = new RegistryLoginConfig
      {
        Username = "user1",
        Password = "pass1",
        Server = "https://registry.example.com"
      };
      var result = await driver.LoginAsync(Ctx, config);

      Assert.True(result.Success);
      var requests = mock.GetRequests();
      var post = requests.First(r => r.Method == "POST" && r.Path.Contains("/auth"));
      Assert.Contains("user1", post.Body);
      Assert.Contains("pass1", post.Body);
      Assert.Contains("registry.example.com", post.Body);
    }

    [Fact]
    public async Task LoginAsync_InvalidCredentials_ReturnsInvalidCredentials()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/auth", 401,
          @"{""message"":""unauthorized""}");

      var config = new RegistryLoginConfig
      {
        Username = "bad",
        Password = "wrong",
        Server = "https://registry.example.com"
      };
      var result = await driver.LoginAsync(Ctx, config);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Auth.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task LoginAsync_ServerError_ReturnsLoginFailed()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/auth", 500,
          @"{""message"":""internal error""}");

      var config = new RegistryLoginConfig
      {
        Username = "user1",
        Password = "pass1",
        Server = "https://registry.example.com"
      };
      var result = await driver.LoginAsync(Ctx, config);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Auth.LoginFailed, result.ErrorCode);
    }

    [Fact]
    public async Task LoginAsync_DefaultServer_UsesDockerHub()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/auth", 200, @"{""Status"":""Login Succeeded""}");

      var config = new RegistryLoginConfig
      {
        Username = "user1",
        Password = "pass1",
        Server = null
      };
      var result = await driver.LoginAsync(Ctx, config);

      Assert.True(result.Success);
      var requests = mock.GetRequests();
      var post = requests.First(r => r.Method == "POST" && r.Path.Contains("/auth"));
      Assert.Contains("https://index.docker.io/v1/", post.Body);
    }

    [Fact]
    public async Task LogoutAsync_ReturnsSuccess()
    {
      var (driver, _) = CreateDriver();

      var result = await driver.LogoutAsync(Ctx);

      Assert.True(result.Success);
    }

    [Fact]
    public async Task LogoutAsync_WithServer_ReturnsSuccess()
    {
      var (driver, _) = CreateDriver();

      var result = await driver.LogoutAsync(Ctx, "https://registry.example.com");

      Assert.True(result.Success);
    }
  }
}
